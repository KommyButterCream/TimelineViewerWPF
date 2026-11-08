using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Viewer.Services
{
    public interface IFileDialogService
    {
        string? SelectTraceFile();
    }
}
