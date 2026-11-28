# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `AslHelp.Engines` with `IMemberPath`, the path contract an engine library implements. Its `Member`, `Item`, `Count` and `Value<T>` steps return a `Result`. `PathKey` is an index or key and `StructValue` is a struct by member name. Extension members on `Result<IMemberPath>` chain the steps.
- `IWatcherSource` and `IWatcherSource<T>` in `AslHelp.Engines` for an object that a host can create a watcher from.
- `AslHelp.Engines.Unreal` with `ObjectReader`, `StructReader` and `IPropertyReader` for reading names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. The caller picks the storage shape with `FlatNameTable`, `IndirectNameTable` or `ChunkedNameTable` behind `INameTable` and `PointerObjectArray`, `IndirectObjectArray`, `ItemObjectArray` or `ChunkedObjectArray` behind `IObjectArray`. For properties it picks `ObjectPropertyReader` through 4.24 or `FieldPropertyReader` from 4.25 behind `IPropertyReader`. It passes the offsets as layout records.
- The readers return `Result<T>`. A failed read has the address, the layout field to check and the cause in its message.
- `UnrealProfile.Parse` in `AslHelp.Engines.Unreal` reads the `unreal3` and `unreal4` profile documents of the `measure` tool. A document with missing offsets or globals fails with all of them in 1 message.
- `UnrealGame.Attach` finds the globals of a profile in a running game and builds the readers for its shapes. `UnrealGame.GWorld` is the root at the world.
- `UnrealGame.ClassOf` returns the real class of an object and `UnrealGame.FindClass` finds a loaded class by name. `UnrealClass.FindProperty` finds a property on the class or one of its super classes.
- `game.GWorld.Path` is an `UnrealPath`, which implements `IMemberPath`. When the object is there, a step fails at once on a member the class doesn't have or on a wrong `T`. When it isn't, the step works and each read fails until the object is there. Each read looks the member up on the real class of the object and goes through an object pointer or into a struct by the kind of the property. The boxed `Watcher<object?>` of a path gives the value in the type of the property it finds at read time.
- Paths and `UnrealGame.Read<T>` read the 9 number properties, `ByteProperty`, `BoolProperty` with bitfields, `EnumProperty`, `NameProperty`, `StrProperty` and the object properties. Paths also go into a `StructProperty`. A profile without the offsets of a kind still attaches and only reads of that kind fail.
- `UnrealGame.Read<T>` reads the value of 1 property. `UnrealProperty.Accept` passes a property to a `PropertyVisitor<TResult>`, which gets `VisitValue<T>`, `VisitEnum`, `VisitStruct`, `VisitCollection` or `VisitUnsupported`.
- Paths and `UnrealGame.Read<T>` read `ArrayProperty` as an array, `SetProperty` as a `HashSet<T>` and `MapProperty` as a `Dictionary<TKey, TValue>`. `UnrealPath.Item(key)` goes into 1 element and `UnrealPath.Count()` reads the number of elements. A whole array fails above 1024 elements. A set or map fails above 1024 slots, used or free.
- `WeakObjectProperty` reads as the `Address` of its object or 0 when the serial number of the item doesn't match. It needs 4.11 or later. `IObjectArray.ReadItem` returns the object and the serial number at an index and `ObjectItemLayout` takes the offset of the serial number.
- A `StructProperty` reads as a `StructValue` with its members by name, both from a boxed watcher and from `Value<StructValue>()`. One whose members are all numbers of 1 class, like `FVector`, also reads as an array of that number. `LargeWorldCoordinatesRealProperty` reads as `double`.
- `UnrealGame.GEngine` is the root at the engine object. A profile document needs `globals.GEngine`.
- `UnrealGame.FindPackage` and `GetPackages` find loaded packages. `UnrealPackage` has `FindClass` and `GetClasses`. `UnrealClass` gets `GetProperties`, `GetInstances` and `GetDerivedClasses`.
- `UnrealObject` is a live object from `GetInstances` with a `Path` that starts at it.
- `UnrealGame.FindSubsystem` returns an `UnrealRoot` at the 1st live object of a subsystem class on 4.22 or later.
- `ProfileCatalog<TProfile>` in `AslHelp.Engines` picks the profile document for a `ProfileTarget` of any engine. The engine passes its mapper and its version parser, which gives the numbers of a `ProfileVersion`. `Select` takes the document that lists the version, its line or a version that starts with it, like 5.8.4 for 5.8. `ProfileGuessHandling.Allow` guesses the nearest patch of the same minor or the highest lower version of the same major with a `Warning`. `Disallow` fails with the version a guess would take. A document added later wins, so a consumer document replaces a shipped document. `ProfileReader` reads the parts every document has for the mapper of an engine and fails a document with a root key that the reader and the mapper don't read or an unread offset in a type that misses an optional offset.
- `AslHelp.Engines.Unreal` ships the measured documents of 4.0 to 5.8 and UDK 2010-09. `UnrealProfile.CreateCatalog` returns a catalog with them.
- `UnrealTarget.Detect` reads the engine version from the executable and builds the key like `win-x64-shipping`. A caller can pass the version and the configuration instead.
- `UnrealGame.Attach(memory, module)` and `Attach(memory, module, catalog)` detect the target. They and the overload with a target pick a document and check that name 0 reads `None`. A document that doesn't fit fails with the detected version. `UnrealGame.Profile` and `UnrealGame.Selection` show what attach used.
