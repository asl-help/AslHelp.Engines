# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `AslHelp.Engines.Unreal` with `ObjectReader` and `StructReader` for reading names, objects and structs from Unreal Engine 3, 4 and 5 games. The caller picks the storage shape with `FlatNameTable`, `IndirectNameTable` or `ChunkedNameTable` behind `INameTable` and `PointerObjectArray`, `IndirectObjectArray`, `ItemObjectArray` or `ChunkedObjectArray` behind `IObjectArray`. It passes the offsets as layout records.
- The readers return `Result<T>`. A failed read has the address, the layout field to check and the cause in its message.
