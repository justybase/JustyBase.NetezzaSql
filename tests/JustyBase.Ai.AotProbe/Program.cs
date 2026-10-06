using System.ComponentModel;
using GitHub.Copilot;
using JustyBase.Ai.Ports;
using Microsoft.Extensions.AI;

var function = AIFunctionFactory.Create(
    ([Description("The name to greet")] string name) => $"Hello {name}",
    name: "probe",
    description: "NativeAOT probe tool");

var sessionConfig = new SessionConfig
{
    ClientName = "JustyBase.Ai.AotProbe",
    Model = "auto",
    Streaming = true,
    Tools = [(AIFunctionDeclaration)function],
    AvailableTools = [$"custom:{function.Name}"],
};

var settings = new ChatSettings
{
    EnableAiChat = true,
    AiChatOpenAiCompatibleEndpoint = "http://127.0.0.1:1/v1",
};

var message = new ChatMessage(ChatRole.User, "aot probe");

Console.WriteLine($"tool={function.Name} schema={function.JsonSchema.ValueKind}");
Console.WriteLine($"tools={sessionConfig.Tools.Count} endpoint={settings.AiChatOpenAiCompatibleEndpoint} role={message.Role}");
Console.WriteLine("AOT-OK");
