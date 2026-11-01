using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;
using PetSitter.Controllers;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class SeedE2eSecurityTests
    {
        [Fact]
        public async Task SeedE2eData_InProductionWithoutFlag_ReturnsNotFound()
        {
            // Arrange: Production environment, cờ E2ESeed:Enabled = false
            var envMock = new Mock<IWebHostEnvironment>();
            envMock.Setup(e => e.EnvironmentName).Returns("Production");

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

            var dbMock = new Mock<IMongoDatabase>();
            var dbNamespace = new DatabaseNamespace("PetSitterDb_test");
            dbMock.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);
            var context = new PetSitterContext(dbMock.Object);

            var bookingServiceMock = new Mock<IBookingService>();
            var tokenServiceMock = new Mock<ITokenService>();

            var controller = new AdminController(
                context,
                bookingServiceMock.Object,
                tokenServiceMock.Object,
                env: envMock.Object,
                config: configMock.Object
            );

            // Act
            var result = await controller.SeedE2eData();

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task SeedE2eData_OnProductionDatabaseName_ReturnsBadRequest()
        {
            // Arrange: Development environment, nhưng Database name là DB thật (PetSitterDb_prod)
            var envMock = new Mock<IWebHostEnvironment>();
            envMock.Setup(e => e.EnvironmentName).Returns("Development");

            var configMock = new Mock<IConfiguration>();

            var dbMock = new Mock<IMongoDatabase>();
            var dbNamespace = new DatabaseNamespace("PetSitterDb_prod"); // Không kết thúc bằng _e2e hoặc _test
            dbMock.Setup(d => d.DatabaseNamespace).Returns(dbNamespace);
            var context = new PetSitterContext(dbMock.Object);

            var bookingServiceMock = new Mock<IBookingService>();
            var tokenServiceMock = new Mock<ITokenService>();

            var controller = new AdminController(
                context,
                bookingServiceMock.Object,
                tokenServiceMock.Object,
                env: envMock.Object,
                config: configMock.Object
            );

            // Act
            var result = await controller.SeedE2eData();

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }
}

