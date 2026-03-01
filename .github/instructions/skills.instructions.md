---
applyTo: '**'
---

# Skill Discovery — SkillMcp

This repository ships a local skill library at `.github/skills/` (index: `.github/skills/skills.json`).
**Before starting any task, select the matching skill(s) below and read the full
`.github/skills/<name>/SKILL.md` file using the `read_file` tool.** Apply every rule,
pattern, and constraint documented in that file for the duration of the task.

---

## Skill Routing Table

Match the task to one or more skills and load their `SKILL.md` before proceeding.

| Task type | Skill to load |
|-----------|--------------|
| Create ASP.NET Minimal API endpoints with proper OpenAPI documentation | `aspnet-minimal-api-openapi` |
| Containerize an ASP.NET Core project by creating Dockerfile and .dockerfile files customized for the project. | `containerize-aspnetcore` |
| Generate a map of all files relevant to a task before making changes | `context-map` |
| Prompt and workflow for generating conventional commit messages using a structured XML format. Guides users to create standardized, descriptive commit messages in line with the Conventional Commits specification, including instructions, examples, and validation. | `conventional-commit` |
| Create a README.md file for the project | `create-readme` |
| Create a new specification file for the solution, optimized for Generative AI consumption. | `create-specification` |
| Get best practices for C# async programming | `csharp-async` |
| Ensure that C# types are documented with XML comments and follow best practices for documentation. | `csharp-docs` |
| Get best practices for XUnit unit testing, including data-driven tests | `csharp-xunit` |
| Ensure .NET/C# code meets best practices for the solution/project. | `dotnet-best-practices` |
| Review the C#/.NET code for design pattern implementation and suggest improvements. | `dotnet-design-pattern-review` |
| Get best practices for Entity Framework Core | `ef-core` |
| Comprehensive technology-agnostic prompt for analyzing and documenting project folder structures. Auto-detects project types (.NET, Java, React, Angular, Python, Node.js, Flutter), generates detailed blueprints with visualization options, naming conventions, file placement patterns, and extension templates for maintaining consistent code organization across diverse technology stacks. | `folder-structure-blueprint-generator` |
| Execute git commit with conventional commit message analysis, intelligent staging, and message generation. Use when user asks to commit changes, create a git commit, or mentions "/commit". Supports: (1) Auto-detecting type and scope from changes, (2) Generating conventional commit messages from diff, (3) Interactive commit with optional type/scope/description overrides, (4) Intelligent file staging for logical grouping | `git-commit` |
| Create optimized multi-stage Dockerfiles for any language or framework | `multi-stage-dockerfile` |
| Generate a complete, production-ready application from an OpenAPI specification | `openapi-to-application-code` |
| Plan a multi-file refactor with proper sequencing and rollback steps | `refactor-plan` |
| Review and refactor code in your project according to defined instructions | `review-and-refactor` |
| Universal SQL code review assistant that performs comprehensive security, maintainability, and code quality analysis across all SQL databases (MySQL, PostgreSQL, SQL Server, Oracle). Focuses on SQL injection prevention, access control, code standards, and anti-pattern detection. Complements SQL optimization prompt for complete development coverage. | `sql-code-review` |
| Universal SQL performance optimization assistant for comprehensive query tuning, indexing strategies, and database performance analysis across all SQL databases (MySQL, PostgreSQL, SQL Server, Oracle). Provides execution plan analysis, pagination optimization, batch operations, and performance monitoring guidance. | `sql-optimization` |
| Comprehensive technology stack blueprint generator that analyzes codebases to create detailed architectural documentation. Automatically detects technology stacks, programming languages, and implementation patterns across multiple platforms (.NET, Java, JavaScript, React, Python). Generates configurable blueprints with version information, licensing details, usage patterns, coding conventions, and visual diagrams. Provides implementation-ready templates and maintains architectural consistency for guided development. | `technology-stack-blueprint-generator` |
| Ask Copilot what files it needs to see before answering a question | `what-context-needed` |

---

## Mandatory Workflow

1. **Identify** which skill(s) apply from the table above.
2. **Read** `.github/skills/<name>/SKILL.md` in full using `read_file` before writing any code or
   documentation. Multiple skills may be loaded in parallel.
3. **Apply** every rule, pattern, naming convention, and constraint from the skill for
   the entire response. Do not apply partial rules.
4. If no skill matches, proceed with the rules in `.github/copilot-instructions.md` only.

---

## Skill Paths (quick reference)

```
.github/skills/aspnet-minimal-api-openapi/SKILL.md
.github/skills/containerize-aspnetcore/SKILL.md
.github/skills/context-map/SKILL.md
.github/skills/conventional-commit/SKILL.md
.github/skills/create-readme/SKILL.md
.github/skills/create-specification/SKILL.md
.github/skills/csharp-async/SKILL.md
.github/skills/csharp-docs/SKILL.md
.github/skills/csharp-xunit/SKILL.md
.github/skills/dotnet-best-practices/SKILL.md
.github/skills/dotnet-design-pattern-review/SKILL.md
.github/skills/ef-core/SKILL.md
.github/skills/folder-structure-blueprint-generator/SKILL.md
.github/skills/git-commit/SKILL.md
.github/skills/multi-stage-dockerfile/SKILL.md
.github/skills/openapi-to-application-code/SKILL.md
.github/skills/refactor-plan/SKILL.md
.github/skills/review-and-refactor/SKILL.md
.github/skills/sql-code-review/SKILL.md
.github/skills/sql-optimization/SKILL.md
.github/skills/technology-stack-blueprint-generator/SKILL.md
.github/skills/what-context-needed/SKILL.md
```
