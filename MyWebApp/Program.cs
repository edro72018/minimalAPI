using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Rewrite;

// Crea el builder para configurar la aplicación web
var builder = WebApplication.CreateBuilder(args);

// Registra un servicio singleton en memoria para manejar las tareas
builder.Services.AddSingleton<ITaskService>(new InMemoryTaskService());

// Construye la aplicación
var app = builder.Build();

// Middleware de reescritura de URL: redirige /tasks/{algo} a /todos/{algo}
app.UseRewriter(new RewriteOptions().AddRedirect("tasks/(.*)", "todos/$1"));

// Middleware para registrar en consola cada solicitud (inicio y fin)
app.Use(async (context, next) =>
{
    Console.WriteLine($"[{context.Request.Method} {context.Request.Path} {DateTime.UtcNow}] Started.");
    await next(context);
    Console.WriteLine($"[{context.Request.Method} {context.Request.Path} {DateTime.UtcNow}] Finished.");
});

// Middleware para bloquear solicitudes DELETE
app.Use(async (context, next) =>
{
    if (context.Request.Method == "DELETE")
    {
        // Respuesta 403 Forbidden si se intenta eliminar
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("No tienes permiso para borrar.");
    }
    else
    {
        await next(); // Continúa al siguiente middleware
    }
});

// Lista en memoria para los todos
var todos = new List<Todo>();

// Endpoint GET: devuelve todos los todos
app.MapGet("/todos", (ITaskService service) => service.GetTodos());

// Endpoint GET: devuelve un todo por ID, o NotFound si no existe
app.MapGet("/todos/{id}", Results<Ok<Todo>, NotFound> (int id, ITaskService service) =>
{
    var targetTodo = service.GetTodoById(id);
    return targetTodo is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(targetTodo);
});

// Endpoint POST: agrega un nuevo todo
app.MapPost("/todos", (Todo task, ITaskService service) =>
{
    service.AddTodo(task);
    return TypedResults.Created("/todos/{id}", task); // Retorna Created
})
// Filtro de endpoint: valida que la fecha no sea pasada y que no esté completado
.AddEndpointFilter(async (context, next) =>
{
    // Obtiene el Todo enviado
    var taskArgument = context.GetArgument<Todo>(0); 
    var errors = new Dictionary<string, string[]>();

    if (taskArgument.DueDate < DateTime.UtcNow)
    {
        errors.Add(nameof(Todo.DueDate), ["Cannot have due date in the past"]);
    }
    if (taskArgument.IsCompleted)
    {
        errors.Add(nameof(Todo.IsCompleted), ["Cannot add complete todo-"]);
    }

    // Si hay errores, retorna un ValidationProblem con detalles
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    // Si no hay errores, continúa con la ejecución del endpoint
    return await next(context);
});

// Endpoint DELETE: elimina un todo por ID
app.MapDelete("/todos/{id}", (int id, ITaskService service) =>
{
    service.DeleteTodoById(id);
    return TypedResults.NoContent();
});

// Inicia la aplicación
app.Run();


// Modelos y servicios

public record Todo(int Id, string Name, DateTime DueDate, bool IsCompleted);

// Interfaz para servicio de manejo de todos
interface ITaskService
{
    Todo? GetTodoById(int id);
    List<Todo> GetTodos();
    void DeleteTodoById(int id);
    Todo AddTodo(Todo Task);
}

// Implementación del servicio en memoria
class InMemoryTaskService : ITaskService
{
    private readonly List<Todo> _todos = [];

    public Todo AddTodo(Todo task)
    {
        _todos.Add(task); // Agrega a la lista
        return task;
    }

    public void DeleteTodoById(int id)
    {
        // Elimina todos los elementos con ese ID
        _todos.RemoveAll(Task => id == Task.Id);
    }

    public Todo? GetTodoById(int id)
    {
        // Busca un todo por ID
        return _todos.SingleOrDefault(t => id == t.Id);
    }

    public List<Todo> GetTodos()
    {
        // Devuelve toda la lista
        return _todos;
    }
}
