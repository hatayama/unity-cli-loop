# Compile Check Skips Dependents That Name Nothing a Reference Changed

Date: 2026-10-16

## Decision

When an assembly's reference assembly shows its dependents a different API than Unity's last
build, the changed-files check worker also names what the change touches: the simple names of the
types whose rows differ, their members' names, the namespace segments one side lacks, and `new(`
for a constructor a target-typed `new` can reach. `uloop compile-check` then leaves out a dependent
that writes none of those names, in its own sources or in the generator output its last full
compile saved, the same way it leaves out a dependent whose references all kept their surface. The
dependent counts as keeping its surface for the assemblies below it, and an assembly that started
early on the assumption that the reference keeps its surface keeps that compile.

- A change a dependent can feel without writing any name is **unnamed**, and an unnamed change
  compiles every dependent, as before:
  - an assembly-level row;
  - for a type both sides have: its flags or attributes, base type, interfaces, type parameters,
    an instance field of a value type, a parameterless constructor, an indexer;
  - an abstract member added;
  - an operator, a compiler-generated member, or a member C# binds by pattern;
  - every row of an interpolated string handler or collection builder type;
  - every row of a type in `System`, `Microsoft.CodeAnalysis` or a namespace under them, on one
    side or both: the compiler looks these up by name (`IsExternalInit` for `init`, `Index` for
    `^1`), so a dependent uses them without writing their names;
  - rows that only moved, and a change with more than 2000 names.
  
  An unnamed answer carries no names at all, so nothing downstream can narrow by them.
- A type added or removed is named by its type name: a dependent can only reach it by writing that
  name, or through another member whose own row then changes too. An extension method is the
  exception and is checked like a member.
- Anything the check cannot read compiles: a reference it could not compare, a worker answer that
  names nothing, a dependent without sources or with a source it cannot read.
- Whether a reference touches a dependent never decides which reference assembly the dependent
  reads when it compiles for another reason, so the input keys and the compiles are what they were.
- A dependent's identifiers are cut mechanically from its text. Comments, strings and code a `#if`
  leaves out count as well, which can only make it compile more often.

## Context

On a project with 561 assemblies, adding a public property to an assembly that 67 others reference
took about 28.5 seconds after dependents stopped waiting for each other (ADR 0016): about 10 for
the edited assembly and about 18 for the 67 dependents. Measured on that project:

- a specific type or member name of the edited assembly was written by 0 to 2 of the 67 dependents;
- a short common member name was written by 48 to 52;
- a target-typed `new(` was written by 59.

Every dependent ran 29 analyzers, about 16 of them source generators. The generator output the
check had saved existed for only 4 dependents, because a skipped dependent's outputs are removed.
In those 4, no generated code used a name of the edited assembly, and every generator in use
generates from its own assembly's code.

So the dependents that write a changed name are few for a specific name. The others compile to the
same result, and only spend the time.

## Consequences

- Editing a specific type or member of a widely referenced assembly is expected to take 12 to 15
  seconds on that project instead of 28.5. Edits to short common names, constructors and type
  shapes compile about as many dependents as before.
- A dependent left out by names is not compiled, so an error only compiling it would show is
  missed when the names miss it. These are the known gaps:
  - a source generator that generates from a referenced assembly's contents rather than from the
    dependent's own code. A unit with analyzers and no saved generator output - one the check has
    never compiled to the end - is compiled on a named change rather than narrowed, whether it was
    picked up as a dependent or started on its own sources ahead of the reference, and leaving a
    dependent out keeps its saved generator output, so after its first compile the names its
    generators write are read along with its sources (VibeLog `name_index.with_generated`, and
    `name_index.without_generated` for the units compiled for want of that output);
  - a change to the type of an async method builder, which a dependent never names (a change to
    its members is unnamed).
- A project that puts its own extension classes in `System.*` gets every dependent compiled when one
  of their members changes, because the whole namespace is unnamed.
- The members C# binds by pattern include common method names such as `Create`, `Add`, `Count` and
  `Start`, so a signature change to any member of that name compiles every dependent. This costs
  time, never a missed error.
- The VibeLog shows the changed names of each surface check, the references that touched each
  compiled unit, the units left out by names, and how long reading the names took.

## Revisit When

- A dependent compiled by Unity fails on a change this check left it out of. Then add that kind of
  change to the unnamed list, or stop leaving dependents out by names.
- The `name_index` duration in the VibeLog is more than a tenth of a run in which no reference
  changed by names. Then start reading the names only when the first named change arrives.

Amended 2026-10-17 by PR-3 of feature/compile-check-visible: a left-out dependent keeps its outputs
and records, and a unit with analyzers and no saved generator output, whether it was picked up as a
dependent or started on its own sources ahead of the reference, is compiled rather than narrowed.
Amended 2026-10-17 by PR-4 of feature/compile-check-visible: identifiers written with `\u` escapes
are read decoded, so that gap is closed.
