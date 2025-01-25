using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using WebApiTemplate.Models;
using WebApiTemplate.Services;

namespace WebApiTemplate;

public static class ApiEndpoints {
    private static readonly EmailAddressAttribute _emailAddressAttribute = new();

    private static ValidationProblem CreateValidationProblem(IdentityResult result)
    {
        // We expect a single error code and description in the normal case.
        // This could be golfed with GroupBy and ToDictionary, but perf! :P
        Debug.Assert(!result.Succeeded);
        var errorDictionary = new Dictionary<string, string[]>(1);

        foreach (var error in result.Errors)
        {
            string[] newDescriptions;

            if (errorDictionary.TryGetValue(error.Code, out var descriptions))
            {
                newDescriptions = new string[descriptions.Length + 1];
                Array.Copy(descriptions, newDescriptions, descriptions.Length);
                newDescriptions[descriptions.Length] = error.Description;
            }
            else
            {
                newDescriptions = new string[] {error.Description};
            }

            errorDictionary[error.Code] = newDescriptions;
        }

        return TypedResults.ValidationProblem(errorDictionary);
    }

    public static void AddApiEndpoints(this WebApplication app) {
        app.MapGet("/", () => {
            return "hello world!";
        });

        app.MapGet("/createTestUser", async (IUserService userService) => {
            var user = await userService.CreateTestUser();
            return Results.Text($"created user with name: {user.FirstName} {user.LastName}");
        });

        app.MapGet("/GeminiJoke", async () => {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Environment.GetEnvironmentVariable("GEMINI_API_KEY")}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Write a story about a magic backpack." }
                        }
                    }
                }
            };

            string jsonBody = System.Text.Json.JsonSerializer.Serialize(requestBody);

            using var httpClient = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8,  "application/json")
            };

            HttpResponseMessage response = await httpClient.SendAsync(request);

            string responseBody = await response.Content.ReadAsStringAsync();

            return Results.Text(responseBody);
        });

        app.MapPost("/register-v2", async Task<Results<Ok, ValidationProblem>>
            ([FromBody] RegisterRequestDto registration, HttpContext context, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<User>>();

            if (!userManager.SupportsUserEmail)
            {
                throw new NotSupportedException($"{nameof(AddApiEndpoints)} requires a user store with email support.");
            }

            var userStore = sp.GetRequiredService<IUserStore<User>>();
            var emailStore = (IUserEmailStore<User>)userStore;
            var email = registration.Email;

            if (string.IsNullOrEmpty(email) || !_emailAddressAttribute.IsValid(email))
            {
                return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidEmail(email)));
            }

            var user = new User();
            await userStore.SetUserNameAsync(user, email, CancellationToken.None);
            await emailStore.SetEmailAsync(user, email, CancellationToken.None);
            user.FirstName = registration.FirstName;
            user.LastName = registration.LastName;
            var result = await userManager.CreateAsync(user, registration.Password);

            if (!result.Succeeded)
            {
                return CreateValidationProblem(result);
            }

            //await SendConfirmationEmailAsync(user, userManager, context, email);
            return TypedResults.Ok();
        });
    }
}

public record RegisterRequestDto(string Email, string Password, string FirstName, string LastName);