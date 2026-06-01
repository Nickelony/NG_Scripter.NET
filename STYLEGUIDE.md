# Documentation style guide

This page owns the documentation standard of the repository, and its last section shows the rules applied.

A doc exists so a reader can use the code now.
Check each claim against the code or a test before writing it, because a wordy doc is cheap to fix and a wrong
one is not. Write plain, complete sentences: cut every word a sentence survives without,
but keep its subject, its verb, the word that carries the reasoning, such as `because`, `so` or `unless`,
and every qualifier, unit, bound and failure mode.

## What counts as a doc

A doc is any prose this repository owns: every Markdown file, an XML doc comment, an inline comment,
and a comment in a project, config, script or workflow file.
Read that as kinds of file, so a new file type is in scope without an edit here.

Not a doc: generated output (`bin/`, `obj/`, `TestResults/`, `artifacts/`, `.vs/`, `.codebase-memory/`),
anything vendored such as the GitHub `.gitignore` template, the VB6 sources under `Original Compiler/`,
and a JSON value, since strict JSON has no comments.

## Rules

1. **Lead with one sentence.**
   A `<summary>` is one sentence, and a page's opening sentence says what its subject does.
   A comment's second sentence states an invariant the caller needs, and it goes in `<remarks>`.
2. **Say only the non-obvious, and say it concretely.**
   - If the name and signature already say it, delete the doc; delete beats reword.
     Never restate a type or parameter name, "gets or sets", or framework behavior.
     A property summary may open with "Gets", as the .NET convention does, but it states what the name does not.
   - Name the constraint, edge case, ordering rule or failure mode;
     a sentence that could describe any class is deleted.
   - Do not walk the reader through a neighbor type or another page's topic;
     mention it only where the text is unreadable without it.
3. **Leave out history, process and filler.**
   - State what the code does and what it guards against, never how it got there:
     no "previously", "no longer", "legacy", "migrated" or "used to", and no review, plan, phase or test ID.
   - The [Vale style](.vale/styles/TRNG) bans the padding, history and census phrases it lists,
     such as "simply", "just", "note that" and "in order to".
   - A comment never addresses the reader; a page may say "you" where the imperative does not fit.
4. **Say it once.**
   - A fact has one home: the code, the guide or the readme that owns it.
     Every other mention states the consequence where it applies and links the home.
   - Never count a set that grows ("the three layers", "both hosts") or list its members outside the home that
     owns them; name the family and link the home. A fixed pair, such as the two tags of an element,
     is not a census.
   - A version, count, default or ID range is stated once, on the member, constant or page that owns it.
     Code comments and other pages link that home,
     and an illustrative key, ID or value is labeled as an example.
   - A line number of this repository's sources is never cited; name the symbol instead.
     The VB6 sources never change, so a VB6 citation names its file and lines.
5. **Use one term per concept.** Use the plain word, and the same word everywhere.
   A term this repository coins or narrows is defined once, in the guide that owns the concept,
   and linked where a page first uses it.
6. **Examples are real.** A Before/After pair shows what the fix produces,
   a comparison labels which side to follow, and a measurement says what it counts.
   Prefer an example a test already pins.

## Code comments

- The Stanza rules `.editorconfig` enables lay out `///` comments,
  from the wrap width to the element order and the closing period.
  `./format.ps1` applies them and the build reports what is left,
  so write the words and let the tooling settle the layout.
- Mark a language keyword with `<see langword="..."/>`. `ZA0211` does it for `null`;
  the other keywords are marked by hand.
- Documentation elements follow the order `ZA0212` enforces:
  `summary`, `typeparam`, `param`, `returns`, `value`, `exception`, `remarks`, `example`, `seealso`.
- An `<exception>` names the condition as a fragment, never with "Thrown when".
- A `cancellationToken` parameter reads "A token that can cancel the {operation}."
  An event reads "Raised when/after", and a protected hook the base invokes reads "Invoked when/after".
- A fallback clause after a semicolon takes a comma: `otherwise, <see .../>`.
- CS1591 is off, so a member needs a doc only when it carries a fact its name and signature do not.
  CS1573 is an error in the library, so a doc names every parameter or none.
- A comment on what a member does or guarantees is a `///` doc on the member,
  and a `//` comment sits inside a body. A `//` label over a group of members keeps a blank line below it,
  and a test keeps its `//` comments. `DocumentationGuardTests` fails on a `//` run directly above a member of
  the library or the console host.
- A test needs no XML doc; its name states what it checks.
  A fixture or guard a reader cannot infer from its name earns one line.
- When a fact outgrows `<remarks>`, it moves to the guide that owns it,
  and the member keeps a one-line consequence and a link.

## Pages

- A page under `docs/` owns one topic and reads in one sitting.
  It leads with what the subject does, then a command or an example, then the detail.
- A page names another file only when the reader cannot follow the topic without it,
  and carries no history: no phase IDs, no plan, no "how this came to be".
- A page links another page by its relative path.

Pick the form by how the facts relate:

| The facts | The form |
| --- | --- |
| Stand alone and read in any order | A bulleted list |
| Happen in order | A numbered list |
| Share the same fields, such as a value and its effect | A table |
| Depend on each other through `because`, `so` or `unless` | A short paragraph |
| Describe behavior a reader could misread | A Before/After example |

- A paragraph that strings facts that stand alone is a bulleted list,
  so a reader scans it rather than wades through it.
- A bullet holds one fact as a complete sentence with a verb.
- The bullets of one list share a grammatical shape,
  and either every bullet opens with a bold lead-in or none does.
- A list nests one level at most and never holds a single item.
- Instructions use the imperative. Behavior uses the present tense and the active voice.

## READMEs

The readme is a front page, not a manual:
a one-line summary, the requirements, the commands that build and run the compiler,
then the few facts the commands do not carry.

- Show the command instead of describing it.
- Never inventory types, members or options; the guides own those.
- Keep the prose to about one screen, and link the guide that owns any deeper fact.

## Living documentation

- The live set is `README.md`, the guides under `docs/`, the readmes under
  `TRNGScriptCompiler.Tests/Fixtures/`, `CONTRIBUTING.md`, `CONVENTIONS.md` and this page.
  A change that makes one of them wrong updates it in the same change.
- Nothing else in the tree records history: no changelog, no completed-work log,
  and no plan or review that has served its purpose.
- Superseded material is deleted, not kept in the tree.

## Documentation shape

Length is judgment, not a quota: write as short as the facts allow,
and split a page that stops being a one-sitting read rather than trim a fact.
A comment passage that needs a heading, a list or a second paragraph belongs on a page.
The ceilings bound a single run. Each lives where the tool that checks it reads it, so this page states no value:

- The `max` of a rule under [`.vale/styles/TRNG`](.vale/styles/TRNG) bounds the words of a paragraph outside fences,
  tables and lists (`ParagraphLength.yml`), of a list item (`ListItemLength.yml`) and of a sentence
  (`SentenceLength.yml`). A sentence past its `max` is split.
- A constant of [`DocumentationGuardTests`](TRNGScriptCompiler.Tests/Repository/DocumentationGuardTests.cs)
  bounds the lines of text in an XML doc element, such as a `<summary>`, a `<param>` or a `<returns>`
  (`XmlDocElementLines`), in a `<remarks>`, which holds about one paragraph (`RemarksLines`),
  and in a run of `//` comment lines (`InlineCommentLines`). A line holding only tags does not count.
  The test also reads those Vale rules, so none can lose its `max`.

A longer run is split, or its fact moves to the page that owns it; it is never cut to fit.
A protocol or type name can make a sentence long, and it is never shortened to fit.

- Markdown is wrapped by hand at 110 columns, and text a change does not touch is reflowed when it is next
  edited. `///` comments wrap at the width `stanza_doc_max_line_length` sets,
  and the tooling does the wrapping.
- A hand-wrapped run flows clause to clause:
  a line carries the clause that fills it even when that overshoots the width,
  and the mark closing the clause stays on the line.
  A wrapped line opens on a word, never on a mark or a list bullet.
- US English. Write " - ", never a typographic dash such as an em or en dash.

Vale fails on a longer run, a banned phrase, a British spelling or a typographic dash,
and `DocumentationGuardTests` fails on a longer doc element or comment run and on a Vale rule that holds no `max`.

## Examples

Each snippet illustrates a rule and is not repository text.

### [Rule 1](#rules) - one sentence, the rest in `<remarks>`

```csharp
// Avoid
/// <summary>Splits text into lines. Line terminators are not part of the returned contents.</summary>

// Prefer
/// <summary>Splits text into lines.</summary>
/// <remarks>
/// Line terminators are not part of the returned contents.
/// </remarks>
```

### [Rule 2](#rules) - delete the restatement

```csharp
// Avoid
/// <summary>Creates a location that marks a term in a line.</summary>
/// <param name="line">The 1-based line.</param>
/// <param name="text">The source text of the line.</param>
/// <param name="term">The term to mark, matched without regard to case.</param>
/// <returns>The location of the term.</returns>

// Prefer
/// <summary>Creates a location that marks a term in a line.</summary>
/// <remarks>
/// The term matches without regard to case.
/// </remarks>
```

### [Pages](#pages) - a bullet list for facts that stand alone

```text
// Avoid
Offsets are zero-based UTF-16 code-unit indexes, so a surrogate pair counts as two units, and LF, CRLF, and
lone CR each end a line, with CRLF counted as one terminator, and a line length excludes the terminator.
```

```text
// Prefer
- Offsets are zero-based UTF-16 code-unit indexes, so a surrogate pair counts as two units.
- LF, CRLF, and lone CR each end a line, and CRLF counts as one terminator.
- A line length excludes the terminator.
```

### [Rule 4](#rules) - a family and a link replace a roster

```markdown
// Avoid
The suite holds BinaryUtilitiesTests, CancellationTests, CompilationHostTests and ConsoleCliTests.
```

```markdown
// Prefer
Each test class covers one area, and the [testing guide](docs/TESTING.md) describes the taxonomy.
```

### [Shape](#documentation-shape) - break at a clause end

```text
// Avoid
Cancellation is observed before a file-system mutation starts
, so a `Canceled` result means the file was not written, moved, or deleted.
```

```text
// Prefer
Cancellation is observed before a file-system mutation starts,
so a `Canceled` result means the file was not written, moved, or deleted.
```
