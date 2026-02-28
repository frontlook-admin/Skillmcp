# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (cache layer)
COPY SkillMcp/SkillMcp.csproj SkillMcp/
RUN dotnet restore SkillMcp/SkillMcp.csproj

# Copy source and publish
COPY SkillMcp/ SkillMcp/
RUN dotnet publish SkillMcp/SkillMcp.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install git so the server can auto-clone awesome-copilot if needed
RUN apt-get update && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*

# Copy published output
COPY --from=build /app/publish .

# Configurable skill source repository URL (default: https://github.com/github/awesome-copilot)
# NOTE: "PATH" is intentionally NOT set here — overriding the system PATH var would break the container.
# It is injected at runtime by mcp.json via docker run --env.
ENV URL=""
ENV DICTIONARY=""
ENV PROMT=""

# MCP stdio server — no exposed ports needed; communicates via stdin/stdout
ENTRYPOINT ["/usr/bin/dotnet", "SkillMcp.dll"]
