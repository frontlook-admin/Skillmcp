# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (cache layer)
COPY AgentWorkflowBuilderMcp/AgentWorkflowBuilderMcp.csproj AgentWorkflowBuilderMcp/
RUN dotnet restore AgentWorkflowBuilderMcp/AgentWorkflowBuilderMcp.csproj

# Copy source and publish
COPY AgentWorkflowBuilderMcp/ AgentWorkflowBuilderMcp/
RUN dotnet publish AgentWorkflowBuilderMcp/AgentWorkflowBuilderMcp.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install git so the server can auto-clone awesome-copilot if needed
RUN apt-get update && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*

# Copy published output
COPY --from=build /app/publish .

# Configurable skill source repository URL (default: https://github.com/github/awesome-copilot)
ENV AWESOME_COPILOT_REPO_URL=""

# MCP stdio server — no exposed ports needed; communicates via stdin/stdout
ENTRYPOINT ["dotnet", "AgentWorkflowBuilderMcp.dll"]
