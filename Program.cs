using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins("https://localhost:4200")
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.AllowSynchronousIO = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors();

app.MapWhen(context => context.Request.Method != "OPTIONS", appBuilder => appBuilder.Run(HandleProxy));
// app.MapWhen(context => context.Request.Method == "OPTIONS", appBuilder => appBuilder.Run(HandlePreflight));


app.Run();


async Task HandleProxy(HttpContext context)
{
    var targetUri = BuildTargetUri(context.Request);

    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    using var client = new HttpClient(handler);

    context.Request.EnableBuffering();

    // Create a new HttpRequestMessage based on the original request
    var requestMessage = new HttpRequestMessage();
    await CopyFromOriginalRequestAsync(context, requestMessage);
    requestMessage.RequestUri = targetUri;

    if (requestMessage.Content != null)
    {
        var content = await requestMessage.Content.ReadAsStringAsync();
        // Console.WriteLine($"Request Content: {content}");
        Console.WriteLine($"Request Content: {requestMessage}");
    }

    var responseMessage = await client.SendAsync(requestMessage);

    if (responseMessage.Headers.Contains("Set-Cookie"))
    {
        Console.WriteLine(responseMessage);
    }
    else
    {
        Console.WriteLine("No Set-Cookie header found.");
    }

    // Modify the cookies if needed

    // Forward the rest of the response headers and content to the client
    CopyToOriginalResponse(responseMessage, context);

    // Console.WriteLine("Final Response Headers:");
    // foreach (var header in context.Response.Headers)
    // {
    //     Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
    // }

    await context.Response.WriteAsync(await responseMessage.Content.ReadAsStringAsync());
}

async Task CopyFromOriginalRequestAsync(HttpContext context, HttpRequestMessage requestMessage)
{
    var request = context.Request;

    // Copy the method
    requestMessage.Method = new HttpMethod(request.Method);

    // Copy the request headers
    foreach (var header in request.Headers)
    {
        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
        {
            requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var contentType = context.Request.ContentType ?? "application/json";
    requestMessage.Content = new StringContent(requestBody, Encoding.UTF8, contentType);

    // Important: Reset the original request's body stream position so it can be read by other middleware or handlers
    context.Request.Body.Position = 0;
}

void CopyToOriginalResponse(HttpResponseMessage responseMessage, HttpContext context)
{
    var response = context.Response;

    // Copy the status code
    response.StatusCode = (int)responseMessage.StatusCode;

    // Copy the response headers
    foreach (var header in responseMessage.Headers)
    {
        response.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in responseMessage.Content.Headers)
    {
        response.Headers[header.Key] = header.Value.ToArray();
    }

    if (responseMessage.Headers.TryGetValues("Set-Cookie", out var cookies))
    {
        Console.WriteLine("Original Cookies:");
        foreach (var cookie in cookies)
        {
            Console.WriteLine(cookie);
        }

        var modifiedCookies = cookies.Select(c => c.Replace("SameSite=Lax", "")).ToList();

        Console.WriteLine("Modified Cookies:");
        foreach (var cookie in modifiedCookies)
        {
            Console.WriteLine(cookie);
        }

        context.Response.Headers.Remove("Set-Cookie");
        foreach (var cookie in modifiedCookies)
        {
            var newCookie = cookie;
            if (!newCookie.Contains("Secure"))
            {
                newCookie += "; Secure=True; SameSite=None";
            }
            context.Response.Headers.Append("Set-Cookie", newCookie);
        }
    }


    // Content is copied outside this function in the HandleProxy method
}

Uri BuildTargetUri(HttpRequest request)
{
    string targetAddress = "https://regservice.1c.ru"; // Replace with your backend's address

    // Append the original path and query string to the backend address
    string targetUri = targetAddress + request.Path + request.QueryString;
    // Console.WriteLine(targetUri);
    return new Uri(targetUri);
}






