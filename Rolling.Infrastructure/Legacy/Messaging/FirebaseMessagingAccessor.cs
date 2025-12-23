using FirebaseAdmin.Messaging;

namespace Rolling.Infrastructure.Messaging;

public sealed class FirebaseMessagingAccessor
{
    public FirebaseMessagingAccessor(FirebaseMessaging? messaging)
    {
        Messaging = messaging;
    }

    public FirebaseMessaging? Messaging { get; }
}
