# Repository AI Instructions

This repository follows the shared AlloyFramework and game business code standard. Keep the peer repository's AGENTS.md synchronized whenever these rules change.


# Alloy Unity Code Style

Apply these rules to authored or modified C# in both repositories:

- `D:/WorkSpeace/UnityProject/AlloyFramework`
- `D:/WorkSpeace/UnityProject/game-template/UnityProj`

Treat them as one shared standard. Do not maintain separate framework and business variants.

When modifying an existing file, make the changed code comply. Do not expand a focused task into an unrelated repository-wide style rewrite. A direct user instruction for the current task takes precedence.

## AlloyFramework Editor Menus

Place every AlloyFramework-owned Unity editor tool under the visible root `★AlloyFramework★`.

- Top menu: `★AlloyFramework★/<中文功能名>`
- UI submenu: `★AlloyFramework★/UI/<中文功能名>`
- Hierarchy creation menu: `GameObject/★AlloyFramework★/<中文功能名>`
- Project window menu, when needed: `Assets/★AlloyFramework★/<中文功能名>`
- `CreateAssetMenu`, when used for an AlloyFramework-owned asset: `★AlloyFramework★/<中文功能名>`

Do not introduce a separate `AlloyFramework/...` root. Use Chinese labels for framework tool menu items. Do not move third-party menus into this root.

## Naming

Use English words for identifiers. Do not use pinyin, arbitrary letters, or names that fail to describe their role.

Do not use snake_case. The required `m_` private-field prefix is the sole project-defined underscore exception. Do not use underscores to separate words after the prefix.

Use these forms:

| Symbol | Form | Example |
| --- | --- | --- |
| Class and struct | PascalCase | `GameStartupPipeline` |
| Method, including private method | PascalCase | `LoadStartupAsync` |
| Non-private field | PascalCase | `CurrentState` |
| Non-private static field | PascalCase | `Instance` |
| Property and event | PascalCase | `IsReady`, `Completed` |
| Constant | uppercase letters without word-separating underscores | `DEFAULTPACKAGENAME` |
| Private field, including private static field | `m_` plus camelCase | `m_currentState` |
| Method parameter | camelCase | `cancellationToken` |
| Local variable | camelCase | `targetDefinition` |
| Interface | `I` plus PascalCase | `IUIJumpConfigProvider` |
| Enum type | `E` plus PascalCase | `EUIState` |
| Enum member | PascalCase | `LoadingStartupConfig` |

Keep established technical abbreviations readable and consistent with this codebase, such as `UI`, `ID`, `URL`, `AOT` and `SDK`. Prefer `UIManager`, `JumpID` and `AOTMetadata` over inconsistent casing.

Choose names for their semantic role. Avoid generic identifiers such as `a`, `b`, `obj`, `temp`, `data1`, `manager2`, or `handlerX` except for conventional very small mathematical/index scopes where the meaning is immediately clear.

## Runtime Closure Ban

Do not introduce closures in runtime code.

This prohibition includes:

- lambdas that capture a local variable, parameter, or `this`;
- anonymous delegates that capture state;
- local functions that capture surrounding state;
- per-call callbacks created solely to carry captured context.

Prefer named instance or static methods, method groups, cached delegates, explicit context objects, or small state-holder types. A lambda is acceptable only when it is proven non-capturing and does not create runtime allocation on the relevant Unity/runtime target; prefer a named method when there is doubt.

The closure ban applies to runtime framework code, runtime business code, and generated runtime code. Editor-only tooling may use closures when they materially simplify editor logic, but avoid them in frequently executed editor callbacks and generation loops when a named method is clear.

When reviewing existing runtime code, call out a captured lambda even when it is functionally correct.

## Declarations and Layout

Declare only one variable per declaration statement.

```csharp
int width;
int height;
```

Do not write:

```csharp
int width, height;
```

Place all class fields at the beginning of the class, before constructors, properties, events, and methods. Attributes attached to a field remain with that field.

Keep a source line within the editor width. Use 120 characters as the default maximum when the repository has no stricter setting. Wrap method arguments, generic constraints, conditions, fluent chains, interpolated expressions, and declarations at logical boundaries. Do not compress code merely to keep it on one line.

## Documentation and Comments

Write XML documentation for every public method. Include:

- `<summary>` describing the method's purpose;
- `<param>` for every parameter;
- `<returns>` for every non-void return value, including `UniTask` results;
- `<exception>` when a public method intentionally exposes a meaningful validation or state exception.

```csharp
/// <summary>
/// 根据跳转配置打开目标界面。
/// </summary>
/// <param name="jumpID">跳转配置的唯一标识。</param>
/// <param name="cancellationToken">用于取消本次跳转的令牌。</param>
/// <returns>目标界面的运行时句柄。</returns>
public UniTask<UIHandle> JumpAsync(
    int jumpID,
    CancellationToken cancellationToken)
```

Write XML documentation for every public field.

```csharp
/// <summary>
/// 默认资源包名称。
/// </summary>
public const string DEFAULTPACKAGENAME = "DefaultPackage";
```

Document private fields with an inline `//` comment after the declaration, separated by one space. Keep the complete line within the line-width limit; wrap by placing a normal comment immediately above only when an inline comment cannot remain readable.

```csharp
private EGameStartupState m_currentState; // 当前业务启动状态。
```

Inside a method, add `//` comments immediately above meaningful logical blocks. Explain the intent, invariant, or reason for the block rather than translating each statement. Separate major phases such as validation, preparation, commit, and rollback. Do not add comments to self-evident single-line operations.

Use Chinese for project-authored comments and XML documentation unless the user requests another language or an external API requires fixed English wording.

## Generated Code

Apply the same naming and documentation rules to generators maintained by the project and to the source they emit. Include a generated-file warning in generated source and modify the generator instead of manually repairing generated output.

Do not restyle third-party generated code such as Luban output unless the task specifically asks to customize its templates. Keep adapters around third-party code compliant with this skill.

## Completion Check

Before finishing a code change, inspect the touched code for:

- AlloyFramework menu paths outside `★AlloyFramework★`;
- pinyin, meaningless identifiers, snake_case, or incorrect casing;
- private fields missing `m_`;
- enum types missing the `E` prefix;
- runtime closures or captured callbacks;
- multiple variables in one declaration;
- fields declared among methods;
- lines longer than the repository limit or the 120-character fallback;
- missing XML documentation on public methods and public fields;
- missing or misplaced private-field and logical-block comments.

Do not run a compile or test command when the user has explicitly reserved compilation for themselves. Perform static inspection instead and report that compilation was not run.
