﻿using Blueprint.Api.Validation;

namespace Blueprint.Tests.Core.Validation.GreaterThanPropertyAttribute_Tests
{
    using System;

    using Blueprint.Core.Validation;

    using NUnit.Framework;

    public class Given_A_Different_Type_Value
    {
        public class NonComparable { }

        public class Validatable
        {
            public DateTime? PropertyToCheckAgainst { get; set; }

            [GreaterThanProperty("PropertyToCheckAgainst")]
            public int? MustBeGreaterThanProperty { get; set; }
        }

        [Test]
        public void When_Value_Type_Is_Not_Numeric_Then_Exception_Is_Thrown()
        {
            // Arrange
            var validatable = new Validatable
            {
                PropertyToCheckAgainst = DateTime.Now,
                MustBeGreaterThanProperty = 1
            };

            var validator = new BlueprintValidator(new IValidationSource[] { new DataAnnotationsValidationSource() });

            // Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.GetValidationResultsAsync(validatable, null));
        }
    }
}
