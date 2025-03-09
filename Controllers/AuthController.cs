using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ElasticSearchPdfApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ElasticSearchPdfApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;

    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        ILogger<AuthController> logger
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RegisterRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
        public string Role { get; set; }
    }

    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        _logger.LogInformation("Registering user: {Name}", request.Name);
        if (request.Password != request.ConfirmPassword)
        {
            _logger.LogWarning("Passwords do not match for user: {Name}", request.Name);
            return BadRequest(new { message = "Passwords do not match" });
        }

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            _logger.LogWarning("User with email {Email} already exists", request.Email);
            return Conflict(new { message = "User with this email already exists" });
        }

        var user = new User { UserName = request.Name, Email = request.Email };

        await _userManager.AddToRoleAsync(user, request.Role);

        var result = await _userManager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
            _logger.LogInformation("User {Name} registered successfully", request.Name);
            return Ok(new { message = "Registration successful" });
        }

        _logger.LogError("Registration failed for user: {Name}", request.Name);
        return BadRequest(
            new
            {
                message = "Registration failed",
                errors = result.Errors.Select(e => e.Description),
            }
        );
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Logging in user: {Email}", request.Email);
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("Invalid email or password for user: {Email}", request.Email);
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: true,
            lockoutOnFailure: false
        );

        if (result.Succeeded)
        {
            _logger.LogInformation("User {Email} logged in successfully", request.Email);
            var roles = await _userManager.GetRolesAsync(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Email, user.Email),
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(
                "a902e68d466d7def17edfbc6587853bac61cb6af281a9993823b515fcc0479f3634141c6c4457f05835b4a5f146a118076bcc9b113d2964c96c1d535eb6e69283714de54f1b8248fdccb3c7d22826a747e750ca890750ca9df3223318b604ac8e9ba2cce08da7f1db065e31c9329556fcb968d93531aa2fe075df13aed4aa9c5d14d0550238f21037d5b51de550fcbdb25604fe60b364f3c7e67ba9b452bc61a8b097fb9ef64150bc433a7f8ad23052f18e6ef35d76404becb21566f84291820a020f6329eba5449ecc7f9128b53e219d85ee79074fb7ec61ee66bffd96bd46817c226b07e734dcc50305346ea1e18028e08e871ceeb5d11d48c50d9011c456f"
            );
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                ),
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            return Ok(new { message = "Logged in successfully", token = tokenString });
        }

        return Unauthorized(new { message = "Invalid email or password" });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("Logging out user");
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out successfully");
        return Ok(new { message = "Logged out successfully" });
    }
}
