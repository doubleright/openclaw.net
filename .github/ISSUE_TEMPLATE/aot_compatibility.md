---
name: NativeAOT compatibility
about: Report a trim, linker, publish, or runtime-mode compatibility issue
title: "[AOT]: "
labels: ["aot", "compatibility", "bug"]
assignees: ""
---

## What failed?

- [ ] Build
- [ ] Publish
- [ ] Link
- [ ] Trim
- [ ] Runtime startup
- [ ] Optional extension load
- [ ] Other

## Environment

- OS:
- RID:
- .NET SDK version:
- Commit SHA:

## Command run

```bash
paste command here
```

## Publish/build mode

- Runtime mode: `aot` / `jit` / `auto`
- PublishAot enabled: yes / no
- Project or package affected:
- Core surface or optional extension surface:

## Linker or trim warnings

```text
paste warnings here
```

## Expected result

## Actual result

```text
paste error output here
```

## Notes

Please include whether this affects the default NativeAOT-friendly path or only an optional JIT/dynamic/plugin-heavy surface.
