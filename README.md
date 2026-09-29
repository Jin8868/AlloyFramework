# Alloy Framework

Alloy is a reusable Unity client framework distributed as a UPM package.

## Structure

- `Runtime/Core`: pure C# code with no UnityEngine dependency.
- `Runtime/Framework`: Unity runtime framework code.
- `Runtime/Assets`: assets shipped with the framework.
- `Editor`: Unity Editor tooling.
- `Tests`: framework tests.

The host game owns gameplay code and generated data. They are not part of this repository.
