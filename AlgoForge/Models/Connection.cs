namespace AlgoForge.Models
{
    // D10: a connection is between two *claimed* users and is global, not scoped to an
    // event -- MetAtEventId is metadata recording where they met. You cannot connect with
    // an unclaimed attendee: there's no one on the other end to accept.
    //
    // Contact details are never a column here. They live on the Attendee/User records and
    // are only ever *read out* when this row says Accepted (D11).
    public class Connection
    {
        public Guid Id { get; set; }

        public Guid RequesterId { get; set; }
        public ApplicationUser? Requester { get; set; }

        public Guid ReceiverId { get; set; }
        public ApplicationUser? Receiver { get; set; }

        public Guid? MetAtEventId { get; set; }
        public Event? MetAtEvent { get; set; }

        public ConnectionStatus Status { get; set; } = ConnectionStatus.Pending;

        // D12: the receiver accepts or declines from the email, so the link carries its own
        // secret rather than relying on the recipient already being signed in.
        public string ResponseToken { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }

        // Order-normalised copy of the user pair. A unique index on (Low, High) is what
        // stops A->B and B->A both existing; it can't be expressed on Requester/Receiver
        // directly because those record who asked, which we want to keep.
        public Guid PairLowId { get; set; }
        public Guid PairHighId { get; set; }

        public static Connection Create(Guid requesterId, Guid receiverId, Guid? metAtEventId)
        {
            var ordered = requesterId.CompareTo(receiverId) <= 0
                ? (Low: requesterId, High: receiverId)
                : (Low: receiverId, High: requesterId);

            return new Connection
            {
                Id = Guid.NewGuid(),
                RequesterId = requesterId,
                ReceiverId = receiverId,
                MetAtEventId = metAtEventId,
                Status = ConnectionStatus.Pending,
                ResponseToken = Guid.NewGuid().ToString("N"),
                PairLowId = ordered.Low,
                PairHighId = ordered.High
            };
        }

        public Guid OtherUserId(Guid viewerId) => viewerId == RequesterId ? ReceiverId : RequesterId;
    }
}
