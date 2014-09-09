﻿using Elders.Cronus.DomainModelling;
using Elders.Cronus.Sample.IdentityAndAccess.Accounts.Commands;

namespace Elders.Cronus.Sample.IdentityAndAccess.Accounts
{
    public class AccountAppService : AggregateRootApplicationService<Account>,
        IMessageHandler<RegisterAccount>,
        IMessageHandler<ChangeAccountEmail>
    {

        public void Handle(RegisterAccount message)
        {
            Repository.Save(new Account(message.Id, message.Email));
        }

        public void Handle(ChangeAccountEmail message)
        {
            Update(message.Id, user => user.ChangeEmail(message.OldEmail, message.NewEmail));
        }
    }
}
