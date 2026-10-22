// Adapted from RavenDB (https://github.com/IlluminedProject/ravendb), commit 46371e89d5ae3a6202e0018d1a5671b0fc6f2c79
// Copyright RavenDB Ltd.; see LICENSE.txt in this directory

using Raven.Client.Documents.Conventions;
using Raven.Client.ServerWide;

namespace ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;

public sealed class DatabaseOptions {
    public DatabaseOptions(string databaseName) : this(new DatabaseRecord(databaseName)) {
    }

    public DatabaseOptions(DatabaseRecord databaseRecord) {
        DatabaseRecord = databaseRecord;
    }

    public bool SkipCreatingDatabase { get; set; }

    public DocumentConventions? Conventions { get; set; }

    public Uri? LocalServerUrl { get; set; }

    public DatabaseRecord DatabaseRecord { get; }
}