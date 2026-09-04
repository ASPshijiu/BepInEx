# Metadata v107 compatibility branch

This branch is a reproducible BepInEx 6 compatibility build for IL2CPP games that use metadata v107. It starts from the BepInEx 6.0.0-be.785 source commit and updates the already-upstream parser and interop packages instead of carrying a private metadata parser patch.

## Locked supply chain

| Component | Version | Upstream source |
| --- | --- | --- |
| BepInEx | `6abdba47eeebe08552282e7a58ef0f4a9ab60b62` | BepInEx 6.0.0-be.785 baseline |
| Cpp2IL | `2022.1.0-development.1715` | `f92ff8bcc8a05e2d9d263b875a25c4ae476a4a24` |
| Il2CppInterop | `1.5.3-ci.1121` | `81a6f78c8b653e0da4a3420ac4cd00819e8b6292` |
| Il2CppInterop.ReferenceLibs | `1.0.0` | NuGet package lock |
| .NET SDK | `8.0.422` | `global.json` |

`Runtimes/Unity/BepInEx.Unity.IL2CPP/packages.lock.json` records every resolved version and NuGet content hash. The dependency check performs a locked restore before a build is accepted.

The build runner uses the existing system-`git` helper instead of Cake.Git's old Intel-only macOS native library. This keeps build metadata equivalent while allowing the branch to build on Apple Silicon and on CI.

## Reproduce the package

```bash
bash scripts/verify-metadata-v107-dependencies.sh
./build.sh --target Publish --build-type Development --build-id 0
```

The Windows x64 IL2CPP archive is written to `bin/dist/BepInEx-Unity.IL2CPP-win-x64-*.zip`.

## Windows game admission checklist

Do not publish this build as game-tested until every item below passes on a clean Windows copy of the target game.

- Back up the save directory and disable Steam Cloud for the first smoke test.
- Install the Windows x64 IL2CPP archive plus the Unity `6000.5.10` base-library archive required by the target game.
- Set `UnityBaseLibrariesSource = 6000.5.10.zip` and temporarily enable interop regeneration.
- Delete `BepInEx/cache` and `BepInEx/interop`, then start the game once.
- Confirm the log accepts metadata v107, completes Cpp2IL analysis and regenerates the interop assemblies without an access violation.
- Confirm the trainer plugin loads and its health page reports every expected patch and direct API check.
- Close and start the game again to verify the cached startup path.
- Load a disposable save and verify manual save, day-end autosave, match-end save and return-to-menu save before re-enabling Steam Cloud.

The public CI only proves source restore, compilation, package creation and ZIP integrity. It cannot prove Doorstop injection, Windows-native detours, game startup or save/cloud behavior.
