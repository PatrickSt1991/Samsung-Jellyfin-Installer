using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinBootloaderInjector
    {
        public async Task InjectDevLogsAsync(PackageWorkspace ws)
        {
            string index = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(index)) return;

            var html = await File.ReadAllTextAsync(index);

            var script = new StringBuilder();
            script.AppendLine("<script>");
            script.AppendLine("(function(){");
            script.AppendLine($"var ws=new WebSocket('ws://{AppSettings.Default.LocalIp}:54321');");
            script.AppendLine("var s=(t,d)=>{try{ws.send(JSON.stringify({type:t,data:d}))}catch{}};");
            script.AppendLine("console.log=(...a)=>s('log',a); console.error=(...a)=>s('error',a);");
            script.AppendLine("window.onerror=(m,sr,l,c)=>s('error',[m,sr,l,c]);");
            script.AppendLine("})();</script>");

            html = html.Replace("</head>", script + "\n</head>");
            await File.WriteAllTextAsync(index, html);
        }
    }
}
