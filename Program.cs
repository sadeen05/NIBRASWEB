using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.Services;
using NIBRAS.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<NebrasdbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IOfferService, OfferService>();
builder.Services.AddScoped<IOfferStatusService, OfferStatusService>();
builder.Services.AddScoped<IOfferVersionService, OfferVersionService>();
builder.Services.AddScoped<IGridService, GridService>();


builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
