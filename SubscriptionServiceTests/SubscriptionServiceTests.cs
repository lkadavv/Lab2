using Moq;
using Subscription_Service.Models;
using Subscription_Service.Services;
using Subscription_Service.Services.Interfaces;

namespace SubscriptionServiceTests
{
    public class SubscriptionServiceTests
    {
        private readonly Mock<IMemberRepository> _repo = new();
        private readonly Mock<IPaymentService> _payment = new();
        private readonly Mock<INotificationService> _notify = new();

        private SubscriptionService CreateService() =>
            new(_repo.Object, _payment.Object, _notify.Object);

        /// <summary>
        /// Assert.True + Assert.NotNull + Verify: успішна оплата продовжує підписку, активує учасника і зберігає зміни.
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_ReturnsTrue_AndActivatesMember()
        {
            var member = new Member { Id = 1, IsActive = false };
            _repo.Setup(r => r.GetById(1)).Returns(member);
            _payment.Setup(p => p.VerifyPayment(1, 100m)).Returns(true);

            var result = CreateService().RenewSubscription(1, 100m, 30);

            Assert.True(result);
            Assert.True(member.IsActive);
            Assert.NotNull(member.SubscriptionEnd);
            _repo.Verify(r => r.Update(member), Times.Once);
        }

        /// <summary>
        /// Assert.False + Verify(Times.Never): при невдалій оплаті повертається false, дані не оновлюються, сповіщення не надсилається.
        /// </summary>
        [Fact]
        public void RenewSubscription_PaymentFailed_ReturnsFalse_AndDoesNotUpdate()
        {
            _repo.Setup(r => r.GetById(1)).Returns(new Member { Id = 1 });
            _payment.Setup(p => p.VerifyPayment(It.IsAny<int>(), It.IsAny<decimal>())).Returns(false);

            var result = CreateService().RenewSubscription(1, 50m, 30);

            Assert.False(result);
            _repo.Verify(r => r.Update(It.IsAny<Member>()), Times.Never);
            _notify.Verify(n => n.SendNotification(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        /// <summary>
        /// Assert.Throws + Verify(Times.Never): якщо учасника не знайдено, кидається ArgumentException, а оплата не перевіряється.
        /// </summary>
        [Fact]
        public void RenewSubscription_MemberNotFound_ThrowsArgumentException()
        {
            _repo.Setup(r => r.GetById(It.IsAny<int>())).Returns((Member?)null);

            var ex = Assert.Throws<ArgumentException>(() => CreateService().RenewSubscription(99, 100m, 30));

            Assert.Equal("Member not found", ex.Message);
            _payment.Verify(p => p.VerifyPayment(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
        }

        /// <summary>
        /// Параметризований тест ([Theory]/[InlineData]): дата закінчення підписки відповідає кількості оплачених днів.
        /// </summary>
        [Theory]
        [InlineData(7)]
        [InlineData(30)]
        [InlineData(365)]
        public void RenewSubscription_SetsSubscriptionEndAccordingToDays(int days)
        {
            var member = new Member { Id = 1 };
            _repo.Setup(r => r.GetById(1)).Returns(member);
            _payment.Setup(p => p.VerifyPayment(1, It.IsAny<decimal>())).Returns(true);
            var before = DateTime.Now;

            CreateService().RenewSubscription(1, 10m, days);

            var expected = before.AddDays(days);
            Assert.InRange(member.SubscriptionEnd!.Value, expected, expected.AddSeconds(5));
        }

        /// <summary>
        /// Assert.NotEqual: після продовження дата закінчення підписки змінюється.
        /// </summary>
        [Fact]
        public void RenewSubscription_ChangesSubscriptionEnd()
        {
            var oldEnd = DateTime.Now.AddDays(-10);
            var member = new Member { Id = 1, SubscriptionEnd = oldEnd };
            _repo.Setup(r => r.GetById(1)).Returns(member);
            _payment.Setup(p => p.VerifyPayment(1, It.IsAny<decimal>())).Returns(true);

            CreateService().RenewSubscription(1, 10m, 30);

            Assert.NotEqual(oldEnd, member.SubscriptionEnd);
        }

        /// <summary>
        /// Verify(Times.Once) + It.Is(predicate): після продовження надсилається сповіщення "Subscription renewed!" саме цьому учаснику.
        /// </summary>
        [Fact]
        public void RenewSubscription_SendsRenewalNotification()
        {
            _repo.Setup(r => r.GetById(5)).Returns(new Member { Id = 5 });
            _payment.Setup(p => p.VerifyPayment(5, It.IsAny<decimal>())).Returns(true);

            CreateService().RenewSubscription(5, 20m, 30);

            _notify.Verify(n => n.SendNotification(
                It.Is<string>(m => m.Contains("renewed")), 5), Times.Once);
        }

        /// <summary>
        /// Assert.Contains + Assert.DoesNotContain + Assert.NotEmpty: DeactivateExpiredMembers деактивує лише учасників із простроченою підпискою.
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_DeactivatesOnlyExpired()
        {
            var expired = new Member { Id = 1, IsActive = true, SubscriptionEnd = DateTime.Now.AddDays(-1) };
            var valid = new Member { Id = 2, IsActive = true, SubscriptionEnd = DateTime.Now.AddDays(10) };
            var updated = new List<Member>();
            _repo.Setup(r => r.GetAll()).Returns(new[] { expired, valid });
            _repo.Setup(r => r.Update(It.IsAny<Member>())).Callback<Member>(updated.Add);

            CreateService().DeactivateExpiredMembers();

            Assert.NotEmpty(updated);
            Assert.Contains(expired, updated);
            Assert.DoesNotContain(valid, updated);
            Assert.False(expired.IsActive);
            Assert.True(valid.IsActive);
        }

        /// <summary>
        /// Assert.Empty: якщо прострочених підписок немає (або дата не задана), жоден учасник не оновлюється.
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_NoExpired_UpdatesNobody()
        {
            var updated = new List<Member>();
            _repo.Setup(r => r.GetAll()).Returns(new[]
            {
                new Member { Id = 1, IsActive = true, SubscriptionEnd = DateTime.Now.AddDays(5) },
                new Member { Id = 2, IsActive = true, SubscriptionEnd = null }
            });
            _repo.Setup(r => r.Update(It.IsAny<Member>())).Callback<Member>(updated.Add);

            CreateService().DeactivateExpiredMembers();

            Assert.Empty(updated);
        }

        /// <summary>
        /// Verify(Times.Exactly(n)): для двох прострочених учасників надсилається рівно два сповіщення.
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_TwoExpired_SendsExactlyTwoNotifications()
        {
            _repo.Setup(r => r.GetAll()).Returns(new[]
            {
                new Member { Id = 1, SubscriptionEnd = DateTime.Now.AddDays(-2) },
                new Member { Id = 2, SubscriptionEnd = DateTime.Now.AddDays(-5) },
                new Member { Id = 3, SubscriptionEnd = DateTime.Now.AddDays(5) }
            });

            CreateService().DeactivateExpiredMembers();

            _notify.Verify(n => n.SendNotification("Membership expired", It.IsAny<int>()), Times.Exactly(2));
        }

        /// <summary>
        /// Verify(Times.Once) + Verify(Times.Never): для одного простроченого учасника сповіщення не більше одного, а непрострочені не оновлюються.
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_OneExpired_SendsExactlyOneNotification()
        {
            _repo.Setup(r => r.GetAll()).Returns(new[]
            {
                new Member { Id = 1, SubscriptionEnd = DateTime.Now.AddDays(-1) },
                new Member { Id = 2, SubscriptionEnd = DateTime.Now.AddDays(1) }
            });

            CreateService().DeactivateExpiredMembers();

            _notify.Verify(n => n.SendNotification(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
            _repo.Verify(r => r.Update(It.Is<Member>(m => m.Id == 2)), Times.Never);
        }
    }
}
