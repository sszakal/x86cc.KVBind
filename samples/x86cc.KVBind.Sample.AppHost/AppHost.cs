var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var kvbindDatabase = postgres.AddDatabase("kvbind");

builder.AddProject<Projects.x86cc_KVBind_Sample_Api>("api")
    .WithReference(kvbindDatabase)
    .WaitFor(postgres);

builder.Build().Run();
