using AslHelp.Engines.Fixtures.Cli.Hosting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Tags the build image by the hash of its folder and checks that the build host has it.
/// </summary>
/// <remarks>
///     A person builds the image with <c>docker build</c>. The tool never builds it, so every step stays readable and a person can
///     run it by hand.
/// </remarks>
internal static class UnrealImage
{
    /// <summary>
    ///     Represents the image name without its tag.
    /// </summary>
    public const string Name = "asl-help/unreal4-build";

    /// <summary>
    ///     Computes the tag of the image built from <paramref name="imageDirectory"/>.
    /// </summary>
    /// <remarks>
    ///     The tag is the short hash of every file in the folder, so a changed file needs a new image and gets a new tag.
    /// </remarks>
    /// <param name="imageDirectory">The image folder, such as the copy next to the tool.</param>
    /// <returns>
    ///     The image with its tag, such as <c>asl-help/unreal4-build:0123456789abcdef</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="imageDirectory"/> does not exist.</exception>
    public static string Tag(string imageDirectory)
    {
        var key = new CacheKeyBuilder("image").AddDirectory("image", imageDirectory).Build();

        return $"{Name}:{key.ShortHash}";
    }

    /// <summary>
    ///     Throws if <paramref name="host"/> doesn't have <paramref name="image"/>.
    /// </summary>
    /// <param name="host">The host the build steps run in.</param>
    /// <param name="image">The image with its tag from <see cref="Tag"/>.</param>
    /// <param name="imageDirectory">The image folder that <paramref name="image"/> was tagged from, for the command in the message.</param>
    /// <param name="cancellationToken">The token that stops the check.</param>
    /// <returns>
    ///     A task that completes once the image is found.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The host doesn't have the image or isn't ready. For a missing image the message has the <c>docker build</c> command.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task ThrowIfMissingAsync(IBuildHost host, string image, string imageDirectory, CancellationToken cancellationToken)
    {
        if (!await host.HasImageAsync(image, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(UnrealSR.FormatImageMissing(image, imageDirectory));
        }
    }
}
