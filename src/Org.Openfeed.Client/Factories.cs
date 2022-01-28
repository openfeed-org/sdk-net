using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {

    /// <summary>
    /// Static class for creating instances of <see cref="IOpenfeedClient"/>.
    /// </summary>
    public static class OpenfeedFactory {
        /// <summary>
        /// Creates a new instance of <see cref="IOpenfeedClient"/>.
        /// </summary>
        /// <param name="uri">Uri of the server, like ws://openfeed.aws.barchart.com/ws.</param>
        /// <param name="username">Username.</param>
        /// <param name="password">Password.</param>
        /// <param name="listeners">Collection of listeners.</param>
        /// <returns></returns>
        public static IOpenfeedClient CreateClient(Uri uri, string username, string password, OpenfeedListeners listeners, string clientId = null) =>
            new Client(uri, username, password, listeners, clientId);
    }

}
