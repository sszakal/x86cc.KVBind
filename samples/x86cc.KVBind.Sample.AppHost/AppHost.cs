var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var kvbindDatabase = postgres.AddDatabase("kvbind");

var api = builder.AddProject<Projects.x86cc_KVBind_Sample_Api>("api")
    .WithReference(kvbindDatabase)
    .WaitFor(postgres);

builder.AddDockerfile("ui", "../../x86cc.KVBind.Sample.UI")
    .WithHttpEndpoint(port: 5200, targetPort: 80)
    .WaitFor(api);

builder.Build().Run();
