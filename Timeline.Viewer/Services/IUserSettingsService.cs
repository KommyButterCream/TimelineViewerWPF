using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public interface IUserSettingsService
    {
        Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);

        Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
    }
}
