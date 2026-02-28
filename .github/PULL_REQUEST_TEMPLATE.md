## MCP Server: Multi-Repo Skill Installer

This PR adds support for installing skills from multiple repositories in a single run, with ephemeral Docker lifecycle:

- Accepts multiple skill sources via env vars (`SKILL_REPO_1_URL`, `SKILL_REPO_1_FOLDER`, ...)
- Clones each repo to a temp directory, installs required skills, then disposes the clone
- Docker container cleans up all temp clones before exit
- Supports both local and Docker workflows

### Checklist
- [x] Refactored SkillSetupService for multi-repo support
- [x] Updated Dockerfile for multi-repo env vars
- [x] Updated .vscode/mcp.json for multi-repo env vars
- [x] README documents multi-repo usage
- [x] All temp clones are deleted after install

---

**How to use:**

Set env vars for each repo:
```
SKILL_REPO_1_URL=https://github.com/github/awesome-copilot
SKILL_REPO_1_FOLDER=skills
SKILL_REPO_2_URL=https://github.com/frontlook-admin/awesome-copilot
SKILL_REPO_2_FOLDER=skills
```

Run the MCP server (locally or in Docker) and it will install skills from all sources, then clean up.
