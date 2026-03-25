---
name: csharp-guidelines
description: >
  Enforces C# coding guidelines by dennisdoomen (https://github.com/dennisdoomen/CSharpGuidelines).
  Use this skill when writing, reviewing, or refactoring C# code to ensure it follows the established
  conventions for class design, member design, maintainability, naming, performance, framework usage,
  documentation, and layout.
license: >
  Based on the CSharpGuidelines by Arjan Eindhoven & Dennis Doomen, licensed under Creative Commons
  Attribution 4.0 International (CC BY 4.0). Source: https://github.com/dennisdoomen/CSharpGuidelines
---

When writing or reviewing C# code, apply the following guidelines. Severity levels: **1** = must, **2** = should, **3** = may.

## Class Design

- **[AV1000] ⚠️ Single Responsibility** — A class or interface should have a single purpose. A class with `And` in its name likely violates this. Use design patterns to communicate intent.
- **[AV1001] ⚠️ Useful constructors** — Constructors should return a fully usable object without needing extra property-setting afterward.
- **[AV1003] ✅ Small, focused interfaces** — Interfaces should be narrow and clearly named. Separate members by responsibility (Interface Segregation Principle).
- **[AV1004] ✅ Prefer interfaces over base classes for extension points** — This avoids forcing consumers to inherit undesired behavior.
- **[AV1005] ✅ Use interfaces to decouple classes** — Interfaces prevent bidirectional associations, simplify replacement, and enable testability via DI.
- **[AV1008] ✅ Avoid static classes** — Except for extension method containers. Static classes are hard to test in isolation.
- **[AV1010] ⚠️ Don't use `new` to suppress compiler warnings** — This breaks polymorphism (Liskov Substitution). Fix the design instead.
- **[AV1011] ✅ Honor Liskov Substitution** — A derived type must be usable wherever its base type is expected. Never throw `NotImplementedException` from overrides.
- **[AV1013] ⚠️ Don't refer to derived classes from a base class** — This breaks extensibility and proper OOP design.
- **[AV1014] ✅ Avoid exposing dependent objects** — Don't expose objects a class depends on (Law of Demeter). Avoid chains like `a.B.GetC().Foo()`.
- **[AV1020] ⚠️ Avoid bidirectional dependencies** — Two classes should not know each other's internals. Use interfaces and DI to break the cycle.
- **[AV1025] ⚠️ Classes should have state and behavior** — Avoid data-only classes paired with behavior-only static classes. Exception: DTOs and parameter objects.
- **[AV1026] ⚠️ Protect internal state consistency** — Validate public member arguments and guard invariants (e.g., `AssertNotDisposed()`).

## Member Design

- **[AV1100] ⚠️ Properties should be order-independent** — Setting `DataSource` before or after `DataMember` should not matter.
- **[AV1105] ✅ Use a method instead of a property** when the work is expensive, represents conversion, returns different results each call, or causes side effects.
- **[AV1110] ⚠️ Don't use mutually exclusive properties** — This signals two conflicting concepts that should be separate types.
- **[AV1115] ⚠️ One responsibility per method/property/local function** — Each member should do exactly one thing.
- **[AV1125] ✅ Don't expose stateful objects through static members** — Makes testing and refactoring very difficult.
- **[AV1130] ✅ Return read-only collection interfaces** — Return `IEnumerable<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey,TValue>`, etc. instead of mutable collections. Exception: immutable collection types like `ImmutableList<T>`.
- **[AV1135] ⚠️ Strings, collections, and tasks should never be `null`** — Return empty strings, empty collections, `Task.CompletedTask`, or `Task.FromResult()` instead.
- **[AV1140] ✅ Prefer domain-specific value types over primitives** — Wrap ISBN numbers, email addresses, money amounts, etc. in dedicated value objects with validation.

## Miscellaneous Design

- **[AV1200] ✅ Throw exceptions instead of returning status values** — Avoids nested if-statements and unchecked return values.
- **[AV1205] ✅ Throw the most specific exception** — Use `ArgumentNullException` instead of `ArgumentException` when an argument is null.
- **[AV1210] ⚠️ Don't swallow errors with catch `Exception`** — Only catch non-specific exceptions at top-level handlers for logging/graceful shutdown.
- **[AV1215] ✅ Handle exceptions correctly in async code** — Exceptions inside `async`/`await` blocks propagate to the awaiter; exceptions before the async block propagate to the caller.

## Maintainability

- **[AV1500] ⚠️ Methods should not exceed 7 statements** — Break long methods into smaller, focused methods with self-explaining names.
- **[AV1501] ⚠️ Default to `private` members and `internal sealed` types** — Open up visibility deliberately and consciously.
- **[AV1505] ✅ Name assemblies after their contained namespace** — Pattern: `Company.Component.dll`.
- **[AV1506] ✅ Name source files after the type they contain** — Use PascalCase, no underscores, no generic type parameter counts in filename.
- **[AV1515] ⚠️ Don't use magic numbers** — Use named constants or enums instead of literal numeric or string values. Exception: contextually clear literals like `/ 2`.
- **[AV1520] ⚠️ Only use `var` when the type is evident** — Use `var` for anonymous types and when the right-hand side makes the type clear. Never use `var` for built-in types.
  ```csharp
  // Correct
  var customer = new Customer();
  bool isValid = true;
  IQueryable<Order> orders = ApplyFilter(...);

  // Incorrect
  var isValid = true;
  ```
- **[AV1530] ✅ Don't modify the loop variable inside a `for` loop** — Use `break` or `continue` instead.
- **[AV1545] ✅ Use direct assignment instead of `if`/`else`** — Prefer `bool isPositive = value > 0;` over an if/else block. Use `??`, `??=`, `?.`, and ternary operators to express intent clearly.
- **[AV1551] ✅ Call the most-overloaded method from simpler overloads** — Simpler overloads should delegate to the most complete one. The most complete overload should be `virtual` if derived classes need to override it.
- **[AV1555] ⚠️ Avoid named arguments** — Exception: `bool` parameters in external APIs where names add clarity, e.g., `inherit: false`.
- **[AV1561] ⚠️ No more than 3 parameters per method/constructor/delegate** — Use a parameter object or Specification pattern for more. Don't return tuples with more than 2 elements.
- **[AV1562] ⚠️ Don't use `ref` or `out` parameters** — Return compound objects or tuples instead. Exception: TryParse pattern.
- **[AV1570] ⚠️ Prefer `is` patterns over `as` casts** — Pattern matching prevents null reference exceptions and improves readability:
  ```csharp
  // Correct
  if (user is RemoteUser remoteUser) { }

  // Incorrect
  var remoteUser = user as RemoteUser;
  if (remoteUser != null) { }
  ```
- **[AV1580] ✅ Write code that is easy to debug** — Avoid deep nesting of method calls. Break into intermediate variables so debugger breakpoints can be set effectively. Exception: fluent API chains.

## Naming Conventions

- **[AV1701] ⚠️ Use US English** — All identifiers must use American English words. Prefer readability over brevity (`CanScrollHorizontally` > `ScrollableX`).
- **[AV1702] ⚠️ Use correct casing**:
  | Element | Casing | Example |
  |---------|--------|---------|
  | Namespace, Type, Interface, Class, Struct, Enum, Enum member, Constant field, Property, Event, Method, Local function | Pascal | `IBusinessService`, `MaxItems`, `Click` |
  | Private field, Parameter, Local variable | Camel | `listItem`, `typeName` |
  | Tuple element names | Pascal | `(string First, string Last)` |
- **[AV1704] ✅ Don't include numbers in variable or member names** — They usually indicate a missing intention-revealing name.
- **[AV1705] ⚠️ Don't prefix fields** — No `_`, `m_`, `g_`, or `s_` prefixes. A method too large to distinguish fields from locals needs refactoring.
- **[AV1710] ⚠️ Don't repeat the class or enum name in its members** — `Employee.Get()` not `Employee.GetEmployee()`.
- **[AV1720] ✅ Name methods and local functions with verbs or verb-object pairs** — `Show`, `ShowDialog`. Don't use `And` in method names — that signals multiple responsibilities.
- **[AV1725] ✅ Name namespaces using nouns, layers, or features** — e.g., `MyCompany.Commerce.Web`, `Microsoft.VisualStudio.Debugging`. Never include a type name in a namespace.
- **[AV1735] ✅ Name events with a verb or verb phrase** — `Click`, `Deleted`, `Closing`, `Arriving`.
- **[AV1739] ✅ Use `_` for irrelevant lambda parameters** — `button.Click += (_, _) => HandleClick();` (C# 9+).

## Performance

- **[AV1800] ✅ Use `Any()` to check if an `IEnumerable<T>` is empty** — Don't use `Count()` on sequences; it may iterate the entire collection.
- **[AV1820] ⚠️ Only use `async` for I/O-bound operations** — `async` does not run code on a worker thread. Use `Task.Run` for CPU-bound work.

## Framework Usage

- **[AV2201] ⚠️ Use C# type aliases** — Use `string`, `int`, `object` instead of `String`, `Int32`, `Object`. Use `int.Parse()` not `Int32.Parse()`.
- **[AV2207] ✅ Don't hard-code deployment-specific strings** — Use configuration files (`app.config`, `appsettings.json`) for connection strings, server addresses, etc.

## Documentation / Comments

- **[AV2301] ⚠️ Write comments and documentation in US English**.
- **[AV2305] ✅ Document all `public`, `protected`, and `internal` types and members** — XML doc comments enable IntelliSense and documentation generation.
- **[AV2310] ✅ Avoid inline comments** — If you need to explain a block of code with a comment, extract it into a method with a descriptive name.

## Layout

- **[AV2400] ⚠️ Follow common layout rules**:
  - Max line length: 130 characters
  - Indent with 4 spaces (no tabs)
  - One space between keywords and expressions: `if (condition == null)`
  - Spaces around operators: `a + b`, `x == y`
  - Opening and closing curly braces always on their own lines (except simple auto-properties)
  - Don't indent object/collection initializers; initialize each property on a new line
  - Put LINQ statements on one line or align each keyword at the same indentation level
  - Start LINQ statements with all `from` expressions before adding `where`/`select`
- **[AV2406] ⚠️ Place members in a well-defined order**:
  1. Private fields and constants
  2. Public constants
  3. Public static read-only fields
  4. Factory methods
  5. Constructors and finalizer
  6. Events
  7. Public properties
  8. Other methods and private properties (in calling order)
  9. Local functions at the bottom of their containing method
- **[AV2410] ⚠️ Use expression-bodied members only when** the body is a single statement that fits on one line.
