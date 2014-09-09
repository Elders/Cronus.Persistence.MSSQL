using System;
using System.Runtime.Serialization;
using Elders.Cronus.DomainModelling;

namespace Elders.Cronus.Sample.Collaboration.Users
{
    [DataContract(Name = "03ba718c-58b8-46e9-978f-c4675e584929")]
    public class UserId : AggregateRootId
    {
        UserId() { }
        public UserId(Guid id) : base(id) { }
    }
}