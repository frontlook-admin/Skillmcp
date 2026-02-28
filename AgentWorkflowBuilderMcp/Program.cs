using AgentWorkflowBuilderMcp.Services;
using AgentWorkflowBuilderMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ProjectTypeDetector>();
builder.Services.AddSingleton<SkillSetupService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(SetupProjectSkillsTools).Assembly);

await builder.Build().RunAsync();
