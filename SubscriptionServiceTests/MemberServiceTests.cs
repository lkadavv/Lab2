using Moq;
using Subscription_Service.Models;
using Subscription_Service.Services;
using Subscription_Service.Services.Interfaces;

namespace SubscriptionServiceTests
{
    public class MemberServiceTests
    {
        private readonly Mock<IMemberRepository> _repo = new();

        private MemberService CreateService() => new(_repo.Object);

        /// <summary>
        /// Assert.NotNull + Assert.Equal: GetMember повертає учасника з репозиторію.
        /// </summary>
        [Fact]
        public void GetMember_ExistingId_ReturnsMember()
        {
            var member = new Member { Id = 1, Name = "Ivan" };
            _repo.Setup(r => r.GetById(1)).Returns(member);

            var result = CreateService().GetMember(1);

            Assert.NotNull(result);
            Assert.Equal("Ivan", result!.Name);
        }

        /// <summary>
        /// Assert.Null: GetMember повертає null, якщо учасника не знайдено.
        /// </summary>
        [Fact]
        public void GetMember_UnknownId_ReturnsNull()
        {
            _repo.Setup(r => r.GetById(It.IsAny<int>())).Returns((Member?)null);

            Assert.Null(CreateService().GetMember(404));
        }

        /// <summary>
        /// Assert.True: IsActive повертає true для активного учасника.
        /// </summary>
        [Fact]
        public void IsActive_ActiveMember_ReturnsTrue()
        {
            _repo.Setup(r => r.GetById(1)).Returns(new Member { Id = 1, IsActive = true });

            Assert.True(CreateService().IsActive(1));
        }

        /// <summary>
        /// Параметризований тест ([Theory]/[InlineData]) + Assert.Equal: IsActive повертає значення прапорця IsActive учасника.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsActive_ReturnsMemberFlag(bool isActive)
        {
            _repo.Setup(r => r.GetById(2)).Returns(new Member { Id = 2, IsActive = isActive });

            Assert.Equal(isActive, CreateService().IsActive(2));
        }

        /// <summary>
        /// Verify() + Times.Once: GetMember викликає репозиторій рівно один раз.
        /// </summary>
        [Fact]
        public void GetMember_CallsRepositoryOnce()
        {
            _repo.Setup(r => r.GetById(3)).Returns(new Member { Id = 3 });

            CreateService().GetMember(3);

            _repo.Verify(r => r.GetById(3), Times.Once);
        }

        /// <summary>
        /// Verify(Times.AtLeastOnce) + It.Is(predicate): репозиторій запитується з потрібним Id.
        /// </summary>
        [Fact]
        public void IsActive_QueriesRepositoryWithRequestedId()
        {
            _repo.Setup(r => r.GetById(It.IsAny<int>())).Returns((Member?)null);

            CreateService().IsActive(7);

            _repo.Verify(r => r.GetById(It.Is<int>(id => id == 7)), Times.AtLeastOnce);
        }

        /// <summary>
        /// /// Verify(Times.Never) + Verify(Times.Once): MemberService лише читає дані, тому Update не викликається, а репозиторій запитується рівно один раз.
        /// </summary>
        [Fact]
        public void GetMember_DoesNotUpdateRepository()
        {
            _repo.Setup(r => r.GetById(1)).Returns(new Member { Id = 1 });

            CreateService().GetMember(1);

            _repo.Verify(r => r.Update(It.IsAny<Member>()), Times.Never);
            _repo.Verify(r => r.GetById(1), Times.Once);
        }

        /// <summary>
        /// Assert.False: IsActive повертає false для відсутнього учасника (репозиторій повертає null).
        /// </summary>
        [Fact]
        public void IsActive_MemberNotFound_ReturnsFalse()
        {
            _repo.Setup(r => r.GetById(It.IsAny<int>())).Returns((Member?)null);
        
            Assert.False(CreateService().IsActive(99));
        }
    }
}
