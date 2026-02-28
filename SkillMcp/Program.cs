using SkillMcp.Services;
using SkillMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ProjectTypeDetector>();
builder.Services.AddSingleton<SkillSetupService>();
builder.Services.AddSingleton<SkillMcp.Services.SkillMapperService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(SetupProjectSkillsTools).Assembly);

await builder.Build().RunAsync();
