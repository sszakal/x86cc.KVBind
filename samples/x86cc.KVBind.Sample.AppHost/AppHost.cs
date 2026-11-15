var builder = DistributedApplication.CreateBuilder(args);

// A named volume keeps the data across AppHost restarts. The generated password is saved in the AppHost's
// user secrets, so it stays the same and the volume remains readable.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("kvbind-sample-postgres");
var kvbindDatabase = postgres.AddDatabase("kvbind");

var api = builder.AddProject<Projects.x86cc_KVBind_Sample_Api>("api")
    .WithReference(kvbindDatabase)
    .WaitFor(postgres);

builder.AddDockerfile("ui", "../../x86cc.KVBind.Sample.UI")
    .WithHttpEndpoint(port: 5200, targetPort: 80)
    .WaitFor(api);

await builder.Build().RunAsync();
