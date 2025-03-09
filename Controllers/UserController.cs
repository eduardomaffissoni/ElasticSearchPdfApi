using ElasticSearchPdfApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElasticSearchPdfApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class UserController : ControllerBase
    {
        private readonly UserManager<User> _userManager;

        private readonly ILogger<UserController> _logger;

        public UserController(UserManager<User> userManager, ILogger<UserController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            _logger.LogInformation("Getting all users");
            var users = await _userManager.Users.AsNoTracking().ToListAsync();
            var userDtos = users.Select(user => new UserResponseDto
            {
                id = user.Id,
                userName = user.UserName,
                email = user.Email,
                role = _userManager.GetRolesAsync(user).Result.FirstOrDefault(),
            });
            _logger.LogInformation("Users retrieved successfully");
            return Ok(userDtos);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(string id)
        {
            _logger.LogInformation("Getting user with id: {Id}", id);
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                _logger.LogWarning("User with id {Id} not found", id);
                return NotFound();
            }
            _logger.LogInformation("User with id {Id} retrieved successfully", id);
            return Ok(user);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser(UpdateUserRequest request)
        {
            _logger.LogInformation("Creating user: {User}", request.User.UserName);
            var result = await _userManager.CreateAsync(request.User);

            if (request.Role != null)
            {
                await _userManager.AddToRoleAsync(request.User, request.Role);
            }
            else
            {
                await _userManager.AddToRoleAsync(request.User, "User");
            }

            if (result.Succeeded)
            {
                _logger.LogInformation("User created successfully: {User}", request.User.UserName);
                return Ok(result);
            }
            else
            {
                _logger.LogError("User creation failed: {Errors}", result.Errors);
                return BadRequest(result.Errors);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(string id, UpdateUserRequest request)
        {
            _logger.LogInformation("Updating user with id: {Id}", id);
            var existingUser = await _userManager.FindByIdAsync(id);
            if (existingUser == null)
            {
                _logger.LogWarning("User with id {Id} not found", id);
                return NotFound();
            }

            if (request.Role != null)
            {
                var userRoles = await _userManager.GetRolesAsync(existingUser);
                await _userManager.RemoveFromRolesAsync(existingUser, userRoles.ToArray());
                await _userManager.AddToRoleAsync(existingUser, request.Role);
            }

            existingUser.UserName = request.User.UserName;
            existingUser.Email = request.User.Email;

            var result = await _userManager.UpdateAsync(existingUser);
            if (result.Succeeded)
            {
                _logger.LogInformation("User updated successfully: {User}", existingUser.UserName);
            }
            else
            {
                _logger.LogError("User update failed: {Errors}", result.Errors);
            }
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            _logger.LogInformation("Deleting user with id: {Id}", id);
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                _logger.LogWarning("User with id {Id} not found", id);
                return NotFound();
            }
            var result = await _userManager.DeleteAsync(user);
            return Ok(result);
        }

        [HttpPut("{id}/update-password")]
        public async Task<IActionResult> UpdatePassword(
            string id,
            [FromBody] UpdatePasswordRequest request
        )
        {
            _logger.LogInformation("Updating password for user with id: {Id}", id);
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                _logger.LogWarning("User with id {Id} not found", id);
                return NotFound();
            }
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "Password updated successfully for user: {User}",
                    user.UserName
                );
                return Ok(result);
            }
            else
            {
                _logger.LogError("Password update failed: {Errors}", result.Errors);
                return BadRequest(result.Errors);
            }
        }
    }
}
