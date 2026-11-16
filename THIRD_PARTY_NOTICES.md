# Sources and third-party notices

## Project implementation and artwork

The C# implementation, application icon and generic mouse / keyboard / receiver illustrations in this repository are supplied under the project MIT license. These illustrations are schematic and do not claim to depict an exact vendor model.

## Device information and interoperability references

Hardware facts were cross-checked against the public [DAREU Web driver](https://dr.dareu.com/) and a user-provided DAREU Driver 1.5.8.22 installation. The published data is reduced to product identifiers, capabilities, key identifiers / positions / default assignments, receiver bindings and LED matrices used for interoperability. Original web scripts, product descriptions, vendor photos, executables, assemblies, firmware and decompiled source are not included.

The MIT license does not grant rights in DAREU names, trademarks or excluded vendor materials. This is an independent project and is not endorsed by DAREU. Data / protocol provenance is documented here; no claim is made that the vendor has licensed its original resources as open source.

## NuGet dependencies

Dependencies are obtained from NuGet during restore and retain their own licenses:

- [HidSharp 2.6.4](https://www.nuget.org/packages/HidSharp/2.6.4): HID access; see the upstream package license.
- [NAudio.Wasapi / NAudio.Core 2.2.1](https://github.com/naudio/NAudio): MIT.
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK): Microsoft package terms and associated third-party notices.
- [.NET 8](https://github.com/dotnet/runtime): MIT and associated notices.

Portable distributions include runtime files; retain all license / third-party notice files supplied by those packages. Framework-dependent Slim distributions rely on separately installed runtimes.
