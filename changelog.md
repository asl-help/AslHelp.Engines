# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `AslHelp.Engines` with `MemberChain<THop>` and `IMemberResolver<THop>` for a path of members that a script writes 1 name at a time through `dynamic`. An engine library resolves each name and creates the typed watcher for the member at the end.
- `IWatcherSource` in `AslHelp.Engines` for an object that a host can create a watcher from.
- `AslHelp.Engines.Unreal` with `ObjectReader`, `StructReader` and `IPropertyReader` for reading names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. The caller picks the storage shape with `FlatNameTable`, `IndirectNameTable` or `ChunkedNameTable` behind `INameTable` and `PointerObjectArray`, `IndirectObjectArray`, `ItemObjectArray` or `ChunkedObjectArray` behind `IObjectArray`. For properties it picks `ObjectPropertyReader` through 4.24 or `FieldPropertyReader` from 4.25 behind `IPropertyReader`. It passes the offsets as layout records.
- The readers return `Result<T>`. A failed read has the address, the layout field to check and the cause in its message.
- `UnrealProfile.Parse` in `AslHelp.Engines.Unreal` reads the `unreal3` and `unreal4` profile documents of the `measure` tool. A document with missing offsets or globals fails with all of them in 1 message.
- `UnrealGame.Attach` finds the globals of a profile in a running game and builds the readers for its shapes. `UnrealGame.GWorld` is the path that starts at the world.
- `UnrealGame.ClassOf` returns the real class of an object and `UnrealGame.FindClass` finds a loaded class by name. `UnrealClass.FindProperty` finds a property on the class or one of its super classes.
- `UnrealPath.Member` and `Member<T>` build a path of members from `GWorld`. Each hop looks the member up on the real class of the object at read time and goes through an object pointer or into a struct by the kind of the property. `UnrealValuePath<T>` is an `IWatcherSource` and creates a `Watcher<T>` for the value.
- `UnrealGame.GWorldChain` is the `MemberChain<THop>` that starts at the world, so a script writes `game.GWorldChain.AuthorityGameMode.FixtureData.Hp` through `dynamic` and gets an `IWatcherSource`. Its `Watcher<object?>` gives the value in the type of the property it finds at read time.
- Paths and `UnrealGame.Read<T>` read the 9 number properties, `ByteProperty`, `BoolProperty` with bitfields, `EnumProperty`, `NameProperty`, `StrProperty` and the object properties. Paths also go into a `StructProperty`. A profile without the offsets of a kind still attaches and only reads of that kind fail.
- `UnrealGame.Read<T>` reads the value of 1 property. `UnrealProperty.Accept` passes a property to a `PropertyVisitor<TResult>`, which gets `VisitValue<T>`, `VisitEnum`, `VisitStruct`, `VisitCollection` or `VisitUnsupported`.
- `MemberChain<THop>` takes an index like `chain.Items[2]` and passes it to the new `IMemberResolver<THop>.ResolveIndex`.
- Paths and `UnrealGame.Read<T>` read `ArrayProperty` as an array, `SetProperty` as a `HashSet<T>` and `MapProperty` as a `Dictionary<TKey, TValue>`. `UnrealPath[key]` and `UnrealPath.Element<T>(key)` go into 1 element and `UnrealPath.Count` reads the number of elements. A whole array fails above 1024 elements. A set or map fails above 1024 slots, used or free.
- `WeakObjectProperty` reads as the `Address` of its object or 0 when the serial number of the item doesn't match. It needs 4.11 or later. `IObjectArray.ReadItem` returns the object and the serial number at an index and `ObjectItemLayout` takes the offset of the serial number.
- A `StructProperty` whose members are all numbers of 1 class, like `FVector`, reads as an array of that number. `LargeWorldCoordinatesRealProperty` reads as `double`.
- `UnrealGame.GEngine` and `GEngineChain` start at the engine object. A profile document needs `globals.GEngine`.
- `UnrealGame.FindPackage` and `GetPackages` find loaded packages. `UnrealPackage` has `FindClass` and `GetClasses`. `UnrealClass` gets `GetProperties`, `GetInstances` and `GetDerivedClasses`.
- `UnrealObject` is a live object from `GetInstances` with a `Path` and a `Chain` that start at it.
- `UnrealGame.FindSubsystem` returns an `UnrealRoot` at the 1st live object of a subsystem class on 4.22 or later.
