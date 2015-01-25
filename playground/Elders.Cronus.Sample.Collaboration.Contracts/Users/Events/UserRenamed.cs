using System.Runtime.Serialization;
using Elders.Cronus.DomainModeling;

namespace Elders.Cronus.Sample.Collaboration.Users.Events
{
    [DataContract(Name = "64089974-6371-4112-84dc-4326ab3ec52e")]
    public class UserRenamed : IEvent
    {
        UserRenamed() { }

        public UserRenamed(UserId id, string firstName, string lastName)
        {
            Id = id;
            FirstName = firstName;
            LastName = lastName;
        }

        [DataMember(Order = 1)]
        public UserId Id { get; private set; }

        [DataMember(Order = 2)]
        public string FirstName { get; private set; }

        [DataMember(Order = 3)]
        public string LastName { get; private set; }
    }
}