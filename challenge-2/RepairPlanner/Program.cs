using System;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// Initialize configuration from environment
var aiProjectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
	?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT environment variable is required.");

var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? string.Empty;
var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? string.Empty;
var cosmosDatabase = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "FactoryDb";

// Service collection & logging
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

// Register Azure clients
services.AddSingleton(provider => new AIProjectClient(new Uri(aiProjectEndpoint), new DefaultAzureCredential()));
services.AddSingleton(provider => new CosmosClient(cosmosEndpoint, cosmosKey));

// Register app services
services.AddSingleton<CosmosDbService>(provider =>
	new CosmosDbService(
		provider.GetRequiredService<CosmosClient>(),
		cosmosDatabase,
		provider.GetRequiredService<ILogger<CosmosDbService>>()));

services.AddSingleton<IFaultMappingService, FaultMappingService>();

// RepairPlannerAgent requires explicit construction parameters
services.AddSingleton(provider => new RepairPlannerAgent(
	provider.GetRequiredService<AIProjectClient>(),
	provider.GetRequiredService<CosmosDbService>(),
	provider.GetRequiredService<IFaultMappingService>(),
	modelDeploymentName,
	provider.GetRequiredService<ILogger<RepairPlannerAgent>>()));

var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILogger<Program>>();

try
{
	logger.LogInformation("Starting Repair Planner demo...");

	var agent = provider.GetRequiredService<RepairPlannerAgent>();

	// Ensure the prompt agent version exists in Foundry (may create or update)
	await agent.EnsureAgentVersionAsync();

	// Sample diagnosed fault
	var sampleFault = new DiagnosedFault
	{
		MachineId = "M-123",
		FaultType = "curing_temperature_excessive",
		RootCause = "Heater element drift",
		Severity = "high",
	};

	var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

	logger.LogInformation("Work order created: {WorkOrderNumber} (id={Id})", workOrder.WorkOrderNumber, workOrder.Id);
	Console.WriteLine(JsonSerializer.Serialize(workOrder, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception ex)
{
	var loggerEx = provider.GetService<ILogger<Program>>();
	loggerEx?.LogError(ex, "Unhandled exception running Repair Planner demo");
	Console.Error.WriteLine(ex);
	Environment.ExitCode = 1;
}
