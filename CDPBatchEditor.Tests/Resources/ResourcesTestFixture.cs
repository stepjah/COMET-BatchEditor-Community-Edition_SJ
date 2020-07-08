// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ResourcesTestFixture.cs" company="RHEA System S.A.">
//    Copyright (c) 2015-2020 RHEA System S.A.
//
//    Author: Nathanael Smiechowski, Alex Vorobiev, Alexander van Delft, Kamil Wojnowski, Sam Gerené
//
//    This file is part of CDP4 Batch Editor. 
//    The CDP4 Batch Editor is a commandline application to perform batch operations on a 
//    ECSS-E-TM-10-25 Annex A and Annex C data source
//
//    The CDP4 Batch Editor is free software; you can redistribute it and/or
//    modify it under the terms of the GNU Lesser General Public
//    License as published by the Free Software Foundation; either
//    version 3 of the License, or any later version.
//
//    The CDP4 Batch Editor is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//    GNU Affero General License for more details.
//
//    You should have received a copy of the GNU Affero General License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace CDPBatchEditor.Tests.Resources
{
    using System;
    using System.Resources;

    using CDPBatchEditor.Resources;

    using NUnit.Framework;

    [TestFixture]
    public class ResourcesTestFixture
    {
        private string path;
        private ResourceLoader resourceLoader;

        [SetUp]
        public void Setup()
        {
            this.path = "CDPBatchEditor.Resources.ascii-art.txt";
            this.resourceLoader = new ResourceLoader();
        }

        [Test]
        public void LoadEmbeddedResource()
        {
            var resource = this.resourceLoader.LoadEmbeddedResource(this.path);
            Assert.IsNotNull(resource);
            Assert.IsNotEmpty(resource);
            Assert.Throws<ArgumentNullException>(() => this.resourceLoader.LoadEmbeddedResource(null));
            Assert.Throws<MissingManifestResourceException>(() => this.resourceLoader.LoadEmbeddedResource("thispathdoesnotexist"));
        }
    }
}
