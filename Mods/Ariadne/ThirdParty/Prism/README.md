# Prism

Ariadne includes the 64-bit Windows dynamic release of Prism 0.17.3:

- Project: https://github.com/ethindp/prism
- Release: https://github.com/ethindp/prism/releases/tag/v0.17.3
- Binary: `Native/Prism/windows-x64/prism.dll`
- SHA-256: `99A73CD24AC777EDE4A5742E62CC3615EC7F5722862C0D9B5E4B3672DCF46CC4`
- License: Mozilla Public License 2.0

The upstream `NOTICE` and third-party license files are included beside this file. At runtime Ariadne extracts the packaged DLL to its versioned directory under the tModLoader save path and loads it by absolute path. It does not modify the tModLoader installation directory.

## Source availability and attribution

Prism is developed by ethindp and the Prism contributors. The bundled DLL is an unmodified upstream binary. The corresponding source is available under MPL-2.0 at no charge:

- https://github.com/ethindp/prism/tree/v0.17.3
- https://github.com/ethindp/prism/archive/refs/tags/v0.17.3.zip

See [the complete credits and source notice](../README.md) for dependency acknowledgments. The full Prism license is `LICENSES/prism/mpl-2.0.txt`; the upstream NOTICE's reference to LICENSE refers to that retained text in this package.
