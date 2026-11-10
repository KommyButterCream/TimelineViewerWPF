using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public interface IWindowMaterialService
    {
        bool TryApply(IntPtr windowHandle, WindowMaterial material, AppTheme theme);
    }
}
