using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HomeManager.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-change-this-key-before-production-123456";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = "HomeManager", ValidAudience = "HomeManager.Mobile",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization(); builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("MobileDevelopment", policy =>
    policy.WithOrigins("http://localhost:8081", "http://127.0.0.1:8081")
        .AllowAnyHeader()
        .AllowAnyMethod()));
var app = builder.Build();
app.UseSwagger(); app.UseSwaggerUI(); app.UseCors("MobileDevelopment"); app.UseAuthentication(); app.UseAuthorization();

app.MapPost("/api/auth/register", async (RegisterRequest input, AppDbContext db, IPasswordHasher<User> hasher) =>
{
    var email = input.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(x => x.Email == email)) return Results.Conflict(new { message = "El correo ya está registrado." });
    var user = new User { Name = input.Name.Trim(), Email = email }; user.PasswordHash = hasher.HashPassword(user, input.Password);
    db.Users.Add(user); await db.SaveChangesAsync(); return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Name, user.Email });
});
app.MapPost("/api/auth/login", async (LoginRequest input, AppDbContext db, IPasswordHasher<User> hasher) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == input.Email.Trim().ToLowerInvariant());
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email) };
    var token = new JwtSecurityToken("HomeManager", "HomeManager.Mobile", claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresIn = 3600, user = new { user.Id, user.Name, user.Email } });
});

var secured = app.MapGroup("/api").RequireAuthorization();
secured.MapGet("/homes", async (ClaimsPrincipal p, AppDbContext db) => await db.HomeMembers.Where(x => x.UserId == p.UserId()).Select(x => new { x.Home.Id, x.Home.Name, x.Role }).ToListAsync());
secured.MapPost("/homes", async (CreateHomeRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    var home = new Home { Name = input.Name.Trim(), TimeZone = input.TimeZone ?? "Europe/Madrid" }; db.Homes.Add(home); db.HomeMembers.Add(new HomeMember { Home = home, UserId = p.UserId(), Role = "Admin" });
    await db.SaveChangesAsync(); return Results.Created($"/api/homes/{home.Id}", new { home.Id, home.Name, home.TimeZone, Role = "Admin" });
});
secured.MapPost("/homes/{homeId:guid}/invitations", async (Guid homeId, InviteRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await db.HomeMembers.AnyAsync(x => x.HomeId == homeId && x.UserId == p.UserId() && x.Role == "Admin")) return Results.Forbid();
    var invite = new HomeInvitation { HomeId = homeId, Email = input.Email.Trim().ToLowerInvariant(), Token = Convert.ToHexString(Guid.NewGuid().ToByteArray()), ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
    db.HomeInvitations.Add(invite); await db.SaveChangesAsync(); return Results.Created($"/api/invitations/{invite.Id}", new { invite.Id, invite.Email, invite.Token, invite.ExpiresAt });
});
secured.MapPost("/invitations/{token}/accept", async (string token, ClaimsPrincipal p, AppDbContext db) =>
{
    var invite = await db.HomeInvitations.SingleOrDefaultAsync(x => x.Token == token && x.AcceptedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow);
    if (invite is null) return Results.NotFound();
    if (!await db.HomeMembers.AnyAsync(x => x.HomeId == invite.HomeId && x.UserId == p.UserId())) db.HomeMembers.Add(new HomeMember { HomeId = invite.HomeId, UserId = p.UserId(), Role = "Member" });
    invite.AcceptedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
});
secured.MapGet("/homes/{homeId:guid}/members", async (Guid homeId, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid();
    return Results.Ok(await db.HomeMembers.Where(x => x.HomeId == homeId).OrderBy(x => x.User.Name).Select(x => new { x.UserId, x.User.Name, x.User.Email, x.Role }).ToListAsync());
});
secured.MapGet("/homes/{homeId:guid}/tasks", async (Guid homeId, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid();
    return Results.Ok(await db.Tasks.Where(x => x.HomeId == homeId).OrderBy(x => x.DueAt).ThenByDescending(x => x.CreatedAt).Select(x => new { x.Id,x.HomeId,x.CreatedById,x.CreatedAt,x.Title,x.Description,x.DueAt,x.Priority,x.Status,x.AssignedUserId,x.CompletedAt,checklist=x.Checklist.OrderBy(i=>i.Position).Select(i=>new{i.Id,i.Title,i.IsCompleted,i.Position}).ToList() }).ToListAsync());
});
secured.MapPost("/homes/{homeId:guid}/tasks", async (Guid homeId, CreateTaskRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid();
    if (input.AssignedUserId is not null && !await IsMember(db, homeId, input.AssignedUserId.Value)) return Results.BadRequest(new { message = "La persona asignada no pertenece al hogar." });
    var task = new TaskItem { HomeId=homeId, CreatedById=p.UserId(), Title=input.Title.Trim(), Description=input.Description, DueAt=input.DueAt, Priority=input.Priority ?? "Normal", AssignedUserId=input.AssignedUserId };
    task.Checklist = (input.Checklist ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select((title,index) => new TaskChecklistItem { Title=title.Trim(), Position=index }).ToList();
    db.Tasks.Add(task); await db.SaveChangesAsync(); return Results.Created($"/api/homes/{homeId}/tasks/{task.Id}",new { task.Id,task.HomeId,task.Title,task.Description,task.DueAt,task.Priority,task.Status,task.AssignedUserId,checklist=task.Checklist.Select(i=>new{i.Id,i.Title,i.IsCompleted,i.Position}) });
});
secured.MapPatch("/homes/{homeId:guid}/tasks/{taskId:guid}", async (Guid homeId, Guid taskId, UpdateTaskRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid();
    var task=await db.Tasks.SingleOrDefaultAsync(x=>x.Id==taskId&&x.HomeId==homeId); if(task is null)return Results.NotFound();
    if (input.AssignedUserId is not null && !await IsMember(db, homeId, input.AssignedUserId.Value)) return Results.BadRequest(new { message = "La persona asignada no pertenece al hogar." });
    if(input.Title is not null)task.Title=input.Title.Trim(); if(input.Description is not null)task.Description=input.Description; if(input.DueAt.HasValue)task.DueAt=input.DueAt; if(input.Priority is not null)task.Priority=input.Priority; if(input.AssignedUserId.HasValue)task.AssignedUserId=input.AssignedUserId;
    if(input.Status is not null){if(input.Status is not ("Pending" or "InProgress" or "Completed" or "Postponed"))return Results.BadRequest(new{message="Estado de tarea no válido."});task.Status=input.Status;task.CompletedAt=input.Status=="Completed"?DateTimeOffset.UtcNow:null;}
    await db.SaveChangesAsync(); return Results.Ok(task);
});
secured.MapPost("/homes/{homeId:guid}/tasks/{taskId:guid}/checklist", async (Guid homeId, Guid taskId, AddChecklistItemRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid(); var task=await db.Tasks.Include(x=>x.Checklist).SingleOrDefaultAsync(x=>x.Id==taskId&&x.HomeId==homeId); if(task is null)return Results.NotFound();
    var item=new TaskChecklistItem{TaskId=taskId,Title=input.Title.Trim(),Position=task.Checklist.Count};db.TaskChecklistItems.Add(item);await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/tasks/{taskId}/checklist/{item.Id}",new{item.Id,item.TaskId,item.Title,item.IsCompleted,item.Position});
});
secured.MapPatch("/homes/{homeId:guid}/tasks/{taskId:guid}/checklist/{itemId:guid}", async (Guid homeId, Guid taskId, Guid itemId, ToggleChecklistItemRequest input, ClaimsPrincipal p, AppDbContext db) =>
{
    if (!await IsMember(db, homeId, p.UserId())) return Results.Forbid();var item=await db.TaskChecklistItems.SingleOrDefaultAsync(x=>x.Id==itemId&&x.TaskId==taskId&&x.Task.HomeId==homeId);if(item is null)return Results.NotFound();item.IsCompleted=input.IsCompleted;await db.SaveChangesAsync();return Results.Ok(item);
});
MapHomeResource<CalendarEvent, CreateEventRequest>(secured, "/homes/{homeId:guid}/events", db => db.Events, (x, i, u) => { x.Title=i.Title; x.StartsAt=i.StartsAt; x.EndsAt=i.EndsAt; x.IsPrivate=i.IsPrivate; x.CreatedById=u; });
MapHomeResource<MealPlanEntry, CreateMealRequest>(secured, "/homes/{homeId:guid}/meals", db => db.Meals, (x, i, u) => { x.Title=i.Title; x.MealType=i.MealType; x.PlannedFor=i.PlannedFor; x.RecipeId=i.RecipeId; x.CreatedById=u; });
MapHomeResource<Product, CreateProductRequest>(secured, "/homes/{homeId:guid}/products", db => db.Products, (x, i, u) => { x.Name=i.Name; x.Quantity=i.Quantity; x.Unit=i.Unit; x.MinimumQuantity=i.MinimumQuantity; x.Location=i.Location; x.CreatedById=u; });
secured.MapGet("/homes/{homeId:guid}/recipes", async (Guid homeId, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();return Results.Ok(await db.Recipes.Where(x=>x.HomeId==homeId).OrderBy(x=>x.Name).Select(x=>new{x.Id,x.HomeId,x.Name,x.Description,x.Servings,x.PreparationMinutes,ingredients=x.Ingredients.Select(i=>new{i.Id,i.ProductId,i.Name,i.Quantity,i.Unit}).ToList()}).ToListAsync()); });
secured.MapPost("/homes/{homeId:guid}/recipes", async (Guid homeId, CreateRecipeRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var recipe=new Recipe{HomeId=homeId,CreatedById=p.UserId(),Name=input.Name.Trim(),Description=input.Description,Servings=input.Servings,PreparationMinutes=input.PreparationMinutes,Ingredients=input.Ingredients.Select((x,index)=>new RecipeIngredient{Name=x.Name.Trim(),Quantity=x.Quantity,Unit=x.Unit,ProductId=x.ProductId}).ToList()};db.Recipes.Add(recipe);await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/recipes/{recipe.Id}",new{recipe.Id,recipe.HomeId,recipe.Name,recipe.Description,recipe.Servings,recipe.PreparationMinutes,ingredients=recipe.Ingredients.Select(i=>new{i.Id,i.ProductId,i.Name,i.Quantity,i.Unit})}); });
secured.MapPost("/homes/{homeId:guid}/products/{productId:guid}/movements", async (Guid homeId, Guid productId, InventoryMovementRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var existing=await db.InventoryMovements.SingleOrDefaultAsync(x=>x.HomeId==homeId&&x.IdempotencyKey==input.IdempotencyKey);if(existing is not null)return Results.Ok(existing);await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);var product=await db.Products.SingleOrDefaultAsync(x=>x.Id==productId&&x.HomeId==homeId);if(product is null)return Results.NotFound();var result=product.Quantity+input.QuantityDelta;if(result<0)return Results.BadRequest(new{message="El movimiento dejaría el stock en negativo."});product.Quantity=result;var movement=new InventoryMovement{HomeId=homeId,CreatedById=p.UserId(),ProductId=productId,QuantityDelta=input.QuantityDelta,ResultingQuantity=result,Reason=input.Reason,IdempotencyKey=input.IdempotencyKey};db.InventoryMovements.Add(movement);await db.SaveChangesAsync();await transaction.CommitAsync();return Results.Created($"/api/homes/{homeId}/products/{productId}/movements/{movement.Id}",movement); });
secured.MapGet("/homes/{homeId:guid}/shopping-lists", async (Guid homeId, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();return Results.Ok(await db.ShoppingLists.Where(x=>x.HomeId==homeId).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.HomeId,x.Name,x.CreatedAt,items=x.Items.Select(i=>new{i.Id,i.ProductId,i.Name,i.Quantity,i.Unit,i.IsPurchased,i.IsReceived,i.AddedById,i.ActualPrice}).ToList()}).ToListAsync()); });
secured.MapPost("/homes/{homeId:guid}/shopping-lists", async (Guid homeId, CreateShoppingListRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var list=new ShoppingList{HomeId=homeId,CreatedById=p.UserId(),Name=input.Name.Trim()};db.ShoppingLists.Add(list);await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/shopping-lists/{list.Id}",list); });
secured.MapPost("/homes/{homeId:guid}/shopping-lists/{listId:guid}/items", async (Guid homeId, Guid listId, CreateShoppingItemRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();if(!await db.ShoppingLists.AnyAsync(x=>x.Id==listId&&x.HomeId==homeId))return Results.NotFound();if(input.ProductId is not null&&!await db.Products.AnyAsync(x=>x.Id==input.ProductId&&x.HomeId==homeId))return Results.BadRequest(new{message="El producto no pertenece al hogar."});var item=new ShoppingListItem{ShoppingListId=listId,ProductId=input.ProductId,Name=input.Name.Trim(),Quantity=input.Quantity,Unit=input.Unit,AddedById=p.UserId()};db.ShoppingListItems.Add(item);await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/shopping-lists/{listId}/items/{item.Id}",new{item.Id,item.ShoppingListId,item.ProductId,item.Name,item.Quantity,item.Unit,item.IsPurchased,item.IsReceived,item.AddedById,item.ActualPrice}); });
secured.MapPatch("/homes/{homeId:guid}/shopping-lists/{listId:guid}/items/{itemId:guid}", async (Guid homeId, Guid listId, Guid itemId, UpdateShoppingItemRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var item=await db.ShoppingListItems.SingleOrDefaultAsync(x=>x.Id==itemId&&x.ShoppingListId==listId&&x.ShoppingList.HomeId==homeId);if(item is null)return Results.NotFound();item.IsPurchased=input.IsPurchased;item.ActualPrice=input.ActualPrice;await db.SaveChangesAsync();return Results.Ok(item); });
secured.MapPost("/homes/{homeId:guid}/shopping-lists/{listId:guid}/receive", async (Guid homeId, Guid listId, ReceiveShoppingListRequest input, ClaimsPrincipal p, AppDbContext db) => { if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);var items=await db.ShoppingListItems.Where(x=>x.ShoppingListId==listId&&x.ShoppingList.HomeId==homeId&&x.IsPurchased&&!x.IsReceived).ToListAsync();foreach(var item in items){var key=$"{input.IdempotencyKey}:{item.Id}";if(await db.InventoryMovements.AnyAsync(x=>x.HomeId==homeId&&x.IdempotencyKey==key)){item.IsReceived=true;continue;}Product? product=item.ProductId is Guid productId?await db.Products.SingleOrDefaultAsync(x=>x.Id==productId&&x.HomeId==homeId):await db.Products.FirstOrDefaultAsync(x=>x.HomeId==homeId&&x.Name.ToLower()==item.Name.ToLower());if(product is null){product=new Product{HomeId=homeId,CreatedById=p.UserId(),Name=item.Name,Unit=item.Unit,Quantity=0,MinimumQuantity=0};db.Products.Add(product);item.ProductId=product.Id;}product.Quantity+=item.Quantity;db.InventoryMovements.Add(new InventoryMovement{HomeId=homeId,CreatedById=p.UserId(),ProductId=product.Id,QuantityDelta=item.Quantity,ResultingQuantity=product.Quantity,Reason="PurchaseReceipt",IdempotencyKey=key});item.IsReceived=true;}await db.SaveChangesAsync();await transaction.CommitAsync();return Results.Ok(new{received=items.Count(x=>x.IsReceived)}); });
secured.MapGet("/homes/{homeId:guid}/issues", async (Guid homeId, ClaimsPrincipal p, AppDbContext db) => {if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();return Results.Ok(await db.HouseholdIssues.Where(x=>x.HomeId==homeId).OrderBy(x=>x.Status=="Resolved").ThenByDescending(x=>x.CreatedAt).ToListAsync());});
secured.MapPost("/homes/{homeId:guid}/issues", async (Guid homeId, CreateIssueRequest input, ClaimsPrincipal p, AppDbContext db) => {if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();if(input.AssignedUserId is not null&&!await IsMember(db,homeId,input.AssignedUserId.Value))return Results.BadRequest(new{message="La persona responsable no pertenece al hogar."});var issue=new HouseholdIssue{HomeId=homeId,CreatedById=p.UserId(),Title=input.Title.Trim(),Description=input.Description,Location=input.Location,Priority=input.Priority??"Normal",AssignedUserId=input.AssignedUserId};db.HouseholdIssues.Add(issue);await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/issues/{issue.Id}",issue);});
secured.MapPatch("/homes/{homeId:guid}/issues/{issueId:guid}", async (Guid homeId, Guid issueId, UpdateIssueRequest input, ClaimsPrincipal p, AppDbContext db) => {if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var issue=await db.HouseholdIssues.SingleOrDefaultAsync(x=>x.Id==issueId&&x.HomeId==homeId);if(issue is null)return Results.NotFound();if(input.Status is not null){if(input.Status is not ("Pending" or "Repairing" or "Resolved"))return Results.BadRequest(new{message="Estado de incidencia no válido."});issue.Status=input.Status;issue.ResolvedAt=input.Status=="Resolved"?DateTimeOffset.UtcNow:null;}if(input.AssignedUserId.HasValue){if(!await IsMember(db,homeId,input.AssignedUserId.Value))return Results.BadRequest(new{message="La persona responsable no pertenece al hogar."});issue.AssignedUserId=input.AssignedUserId;}await db.SaveChangesAsync();return Results.Ok(issue);});
secured.MapPost("/homes/{homeId:guid}/issues/{issueId:guid}/create-task", async (Guid homeId, Guid issueId, ClaimsPrincipal p, AppDbContext db) => {if(!await IsMember(db,homeId,p.UserId()))return Results.Forbid();var issue=await db.HouseholdIssues.SingleOrDefaultAsync(x=>x.Id==issueId&&x.HomeId==homeId);if(issue is null)return Results.NotFound();if(issue.LinkedTaskId is Guid linkedId)return Results.Ok(new{taskId=linkedId});var task=new TaskItem{HomeId=homeId,CreatedById=p.UserId(),Title=$"Resolver: {issue.Title}",Description=issue.Description,Priority=issue.Priority,AssignedUserId=issue.AssignedUserId,DueAt=DateTimeOffset.UtcNow.AddDays(1)};db.Tasks.Add(task);issue.LinkedTaskId=task.Id;await db.SaveChangesAsync();return Results.Created($"/api/homes/{homeId}/tasks/{task.Id}",new{taskId=task.Id});});
app.MapGet("/health", () => Results.Ok(new { status = "ok" })); app.Run();

static void MapHomeResource<TEntity,TRequest>(RouteGroupBuilder group, string route, Func<AppDbContext,DbSet<TEntity>> set, Action<TEntity,TRequest,Guid> fill) where TEntity : HomeEntity, new()
{
    group.MapGet(route, async (Guid homeId, ClaimsPrincipal p, AppDbContext db) => await db.HomeMembers.AnyAsync(x => x.HomeId == homeId && x.UserId == p.UserId()) ? Results.Ok(await set(db).Where(x => x.HomeId == homeId).OrderByDescending(x => x.CreatedAt).ToListAsync()) : Results.Forbid());
    group.MapPost(route, async (Guid homeId, TRequest input, ClaimsPrincipal p, AppDbContext db) => { if (!await db.HomeMembers.AnyAsync(x => x.HomeId == homeId && x.UserId == p.UserId())) return Results.Forbid(); var entity = new TEntity { HomeId = homeId }; fill(entity,input,p.UserId()); set(db).Add(entity); await db.SaveChangesAsync(); return Results.Created($"{route}/{entity.Id}",entity); });
}
static Task<bool> IsMember(AppDbContext db, Guid homeId, Guid userId) => db.HomeMembers.AnyAsync(x => x.HomeId == homeId && x.UserId == userId);
public partial class Program { }
public record RegisterRequest(string Name,string Email,string Password); public record LoginRequest(string Email,string Password); public record CreateHomeRequest(string Name,string? TimeZone); public record InviteRequest(string Email);
public record CreateTaskRequest(string Title,string? Description,DateTimeOffset? DueAt,string? Priority,Guid? AssignedUserId,string[]? Checklist); public record UpdateTaskRequest(string? Title,string? Description,DateTimeOffset? DueAt,string? Priority,string? Status,Guid? AssignedUserId); public record AddChecklistItemRequest(string Title); public record ToggleChecklistItemRequest(bool IsCompleted); public record CreateEventRequest(string Title,DateTimeOffset StartsAt,DateTimeOffset? EndsAt,bool IsPrivate=false); public record CreateMealRequest(string Title,string MealType,DateOnly PlannedFor,Guid? RecipeId=null); public record CreateProductRequest(string Name,decimal Quantity,string Unit,decimal MinimumQuantity,string? Location); public record CreateShoppingListRequest(string Name); public record RecipeIngredientRequest(string Name,decimal Quantity,string Unit,Guid? ProductId); public record CreateRecipeRequest(string Name,string? Description,int Servings,int PreparationMinutes,RecipeIngredientRequest[] Ingredients); public record InventoryMovementRequest(decimal QuantityDelta,string Reason,string IdempotencyKey); public record CreateShoppingItemRequest(string Name,decimal Quantity,string Unit,Guid? ProductId); public record UpdateShoppingItemRequest(bool IsPurchased,decimal? ActualPrice); public record ReceiveShoppingListRequest(string IdempotencyKey); public record CreateIssueRequest(string Title,string? Description,string? Location,string? Priority,Guid? AssignedUserId); public record UpdateIssueRequest(string? Status,Guid? AssignedUserId);
static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("El token no contiene un identificador de usuario válido.");
    }
}
