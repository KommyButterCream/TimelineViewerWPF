using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed class UserSettingsService : IUserSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        private readonly string _filePath;

        public UserSettingsService()
            : this(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Timeline.Viewer", "settings.json"))
        {
        }

        public UserSettingsService(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            _filePath = Path.GetFullPath(filePath);
        }

        public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                UserSettings? settings = await JsonSerializer.DeserializeAsync<UserSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                return Normalize(settings);
            }
            catch (JsonException)
            {
                return new UserSettings();
            }
            catch (IOException)
            {
                return new UserSettings();
            }
            catch (UnauthorizedAccessException)
            {
                return new UserSettings();
            }
        }

        public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();

            UserSettings normalized = Normalize(settings);

            string directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            string temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        normalized,
                        JsonOptions,
                        cancellationToken);

                    await stream.FlushAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // 임시 파일 정리 실패가 원래 저장 오류를 가리지 않게 합니다.
                }
                catch (UnauthorizedAccessException)
                {
                    // 임시 파일을 삭제할 수 없어도 원래 결과를 유지합니다.
                }
            }
        }

        private static UserSettings Normalize(UserSettings? settings)
        {
            var defaults = new UserSettings();

            if (settings is null || settings.Version != defaults.Version)
            {
                return defaults;
            }

            bool validPosition =
                settings.WindowLeft is double left
                && double.IsFinite(left)
                && settings.WindowTop is double top
                && double.IsFinite(top);

            return settings with
            {
                Theme = Enum.IsDefined(settings.Theme)
                    ? settings.Theme
                    : defaults.Theme,

                WindowWidth =
                    double.IsFinite(settings.WindowWidth)
                    && settings.WindowWidth >= 900
                        ? settings.WindowWidth
                        : defaults.WindowWidth,

                WindowHeight =
                    double.IsFinite(settings.WindowHeight)
                    && settings.WindowHeight >= 560
                        ? settings.WindowHeight
                        : defaults.WindowHeight,

                WindowLeft = validPosition
                    ? settings.WindowLeft
                    : null,

                WindowTop = validPosition
                    ? settings.WindowTop
                    : null,
                MonitorOffsetX = settings.MonitorOffsetX is double x && double.IsFinite(x) ? x : null,
                MonitorOffsetY = settings.MonitorOffsetY is double y && double.IsFinite(y) ? y : null
            };
        }
    }
}
