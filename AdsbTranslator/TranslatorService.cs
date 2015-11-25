using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    public partial class TranslatorService : ServiceBase
    {
        Server server;
        public TranslatorService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            server = new Server();
        }

        protected override void OnStop()
        {
            server.stopServer();
        }
    }
}
