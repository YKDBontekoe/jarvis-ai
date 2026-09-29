using Jarvis.Worker.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddJarvisWorker(builder.Configuration);
await builder.Build().RunAsync();
