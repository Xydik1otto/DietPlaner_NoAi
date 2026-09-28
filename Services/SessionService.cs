using DietPlanner.Models;

namespace DietPlanner.Services;

public class SessionService
{
    public event EventHandler? StateChanged;

    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null;

    public void SignIn(User user)
    {
        CurrentUser = user;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentUser(User? user)
    {
        CurrentUser = user;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => SignOut();
}