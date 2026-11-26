# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `AslHelp.Engines` with `MemberChain<THop>` and `IMemberResolver<THop>` for a path of members that a script writes 1 name at a time through `dynamic`. An engine library resolves each name and creates the typed watcher for the member at the end.
- `IWatcherSource` in `AslHelp.Engines` for an object that a host can create a watcher from.
- `AslHelp.Engines.Unreal` with `ObjectReader`, `StructReader` and `IPropertyReader` for reading names, objects, structs and properties from Unreal Engine 3, 4 and 5 games. The caller picks the storage shape with `FlatNameTable`, `IndirectNameTable` or `ChunkedNameTable` behind `INameTable` and `PointerObjectArray`, `IndirectObjectArray`, `ItemObjectArray` or `ChunkedObjectArray` behind `IObjectArray`. For properties it picks `ObjectPropertyReader` through 4.24 or `FieldPropertyReader` from 4.25 behind `IPropertyReader`. It passes the offsets as layout records.
- The readers return `Result<T>`. A failed read has the address, the layout field to check and the cause in its message.
- `UnrealProfile.Parse` in `AslHelp.Engines.Unreal` reads a profile document of the `measure` tool. A document with missing offsets or globals fails with all of them in 1 message.
- `UnrealGame.Attach` finds the globals of a profile in a running game and builds the readers for its shapes. `UnrealGame.GWorld` reads the world pointer on each call.
