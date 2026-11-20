using JobRadar.Feeds;
using JobRadar.ServiceDefaults;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.AddMessaging(_ => { });
builder.Services.AddJobFeeds(builder.Configuration);

var app = builder.Build();
app.MapDefaultEndpoints();
await app.RunAsync();
