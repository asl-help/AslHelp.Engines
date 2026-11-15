using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using Iced.Intel;
using System;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Finds statics without a public symbol through the code that uses them.
/// </summary>
internal static class UnrealStatics
{
    private const int MostInstructions = 512;
    private const int WindowSize = 128;

    /// <summary>
    ///     Decodes a function and finds the 1st address it reads outside the code that doesn't have a public symbol.
    /// </summary>
    /// <remarks>
    ///     Skips publics, because a security cookie or an import slot has one and the static doesn't.
    /// </remarks>
    /// <param name="pdb">The PDB of the player.</param>
    /// <param name="image">The code.</param>
    /// <param name="function">The name of the function in C++, such as <c>Scope::Get</c>.</param>
    /// <returns>
    ///     The RVA of the static or a failure with the function when the PDB doesn't have the function, the code doesn't have it
    ///     or it doesn't read a static.
    /// </returns>
    public static Result<uint> FindThroughFunction(PdbFile pdb, CodeImage image, string function)
    {
        if (!pdb.FunctionRva(function).TryUnwrap(out uint functionRva, out var error))
        {
            return error;
        }

        var section = image.Sections.FirstOrDefault(section => Contains(section, functionRva));

        if (section is null)
        {
            return UnrealStaticError.FunctionStaticNotFound(function, functionRva);
        }

        int start = (int)(functionRva - section.Rva);
        using MemoryCodeReader reader = new(section.Bytes[start..]);
        var decoder = Decoder.Create(image.Is64Bit ? 64 : 32, reader, image.ImageBase + functionRva);

        for (int count = 0; count < MostInstructions && reader.CanReadByte; count++)
        {
            decoder.Decode(out var instruction);

            if (instruction.IsInvalid || instruction.Mnemonic is Mnemonic.Ret or Mnemonic.Int3)
            {
                break;
            }

            var offsets = decoder.GetConstantOffsets(instruction);
            ulong? address = InstructionSites.DisplacementAddress(instruction, offsets, image.Is64Bit)
                ?? InstructionSites.ImmediateAddress(instruction, offsets, image.Is64Bit);

            if (address is { } value && IsStatic(image, value) && !pdb.HasGlobalAt((uint)(value - image.ImageBase)))
            {
                return (uint)(value - image.ImageBase);
            }
        }

        return UnrealStaticError.FunctionStaticNotFound(function, functionRva);
    }

    /// <summary>
    ///     Decodes the code and finds the static that the calls of the default constructor of a type pass as <c>this</c>.
    /// </summary>
    /// <remarks>
    ///     Skips a call that passes anything else, such as an object on the stack.
    /// </remarks>
    /// <param name="pdb">The PDB of the player.</param>
    /// <param name="image">The code.</param>
    /// <param name="type">The name of the type, in the global scope.</param>
    /// <param name="cancellationToken">The token that stops the search before the next section.</param>
    /// <returns>
    ///     The RVA of the static or a failure with the type when the PDB doesn't have the constructor, no call passes a static
    ///     or 2 calls pass different statics.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Result<uint> FindThroughConstructor(PdbFile pdb, CodeImage image, string type, CancellationToken cancellationToken)
    {
        // Build the decorated name, because the PDB lists a constructor only under it. QEAA and QAE are the public members of x64 and x86.
        if (!pdb.FunctionRva($"??0{type}@@{(image.Is64Bit ? "QEAA" : "QAE")}@XZ").TryUnwrap(out uint constructorRva, out var error))
        {
            return error;
        }

        ulong constructor = image.ImageBase + constructorRva;
        var window = new Instruction[WindowSize];
        uint? found = null;

        foreach (var section in image.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using MemoryCodeReader reader = new(section.Bytes);
            var decoder = Decoder.Create(image.Is64Bit ? 64 : 32, reader, image.ImageBase + section.Rva);

            for (int decoded = 0; reader.CanReadByte; decoded++)
            {
                ref var instruction = ref window[decoded % window.Length];
                decoder.Decode(out instruction);

                if (!instruction.IsCallNear
                    || instruction.NearBranchTarget != constructor
                    || PassedAsThis(window, decoded) is not { } address
                    || !IsStatic(image, address))
                {
                    continue;
                }

                uint rva = (uint)(address - image.ImageBase);

                if (found is uint other && other != rva)
                {
                    return UnrealStaticError.ConstructedStaticAmbiguous(type, other, rva);
                }

                found = rva;
            }
        }

        return found is uint result ? result : UnrealStaticError.ConstructedStaticNotFound(type, constructorRva);
    }

    /// <summary>
    ///     Follows the 1st register back from a call to the address that it was loaded with.
    /// </summary>
    /// <param name="window">The last decoded instructions, as a ring.</param>
    /// <param name="call">The number of the call among the decoded instructions.</param>
    /// <returns>
    ///     The address or <see langword="null"/> if the caller starts first or sets the register some other way.
    /// </returns>
    private static ulong? PassedAsThis(Instruction[] window, int call)
    {
        var register = Register.RCX;

        for (int back = call - 1; back >= 0 && back > call - window.Length; back--)
        {
            ref readonly var instruction = ref window[back % window.Length];

            // Stop at a call too, because it can change the register.
            if (instruction.FlowControl is not (FlowControl.Next or FlowControl.ConditionalBranch))
            {
                return null;
            }

            if (instruction.Op0Kind != OpKind.Register || instruction.Op0Register.GetFullRegister() != register)
            {
                continue;
            }

            if (instruction is { Mnemonic: Mnemonic.Mov, Op1Kind: OpKind.Register })
            {
                register = instruction.Op1Register.GetFullRegister();
            }
            else if (instruction.Mnemonic is not (Mnemonic.Cmp or Mnemonic.Push or Mnemonic.Test))
            {
                return LoadedAddress(instruction);
            }
        }

        return null;
    }

    private static ulong? LoadedAddress(in Instruction instruction)
    {
        return instruction switch
        {
            { Mnemonic: Mnemonic.Lea, IsIPRelativeMemoryOperand: true } => instruction.IPRelativeMemoryAddress,
            { Mnemonic: Mnemonic.Lea, MemoryBase: Register.None, MemoryIndex: Register.None } => instruction.MemoryDisplacement32,
            { Mnemonic: Mnemonic.Mov, Op1Kind: OpKind.Immediate32 } => instruction.Immediate32,
            _ => null,
        };
    }

    private static bool IsStatic(CodeImage image, ulong address)
    {
        // Refuse the image base too, because x64 code loads it for a jump table and the headers don't have a static.
        if (address <= image.ImageBase || address - image.ImageBase >= image.ImageSize)
        {
            return false;
        }

        uint rva = (uint)(address - image.ImageBase);

        return !image.Sections.Any(section => Contains(section, rva));
    }

    private static bool Contains(CodeSection section, uint rva)
    {
        return rva >= section.Rva && rva - section.Rva < section.Bytes.Length;
    }
}
