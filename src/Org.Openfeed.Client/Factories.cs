using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {

    public static class OpenfeedFactory {
        public static IOpenfeedClient CreateClient(Uri uri, string username, string password, OpenfeedListeners listeners) =>
            new Client(uri, username, password, listeners);
    }

}
