﻿//  --------------------------------------------------------------------------------------------------------------------
//  <copyright file="IReportGenerator.cs" company="Starion Group S.A.">
//     Copyright (c) 2015-2024 Starion Group S.A.
// 
//     Author: Nathanael Smiechowski, Alex Vorobiev, Alexander van Delft, Sam Gerené
// 
//     This file is part of CDP4-COMET Batch Editor.
//     The CDP4-COMET Batch Editor is a commandline application to perform batch operations on a
//     ECSS-E-TM-10-25 Annex A and Annex C data source
// 
//     The CDP4-COMET Batch Editor is free software; you can redistribute it and/or
//     modify it under the terms of the GNU Lesser General Public
//     License as published by the Free Software Foundation; either
//     version 3 of the License, or any later version.
// 
//     The CDP4-COMET Batch Editor is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//     GNU Lesser General License version 3 for more details.
// 
//     You should have received a copy of the GNU Lesser General License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
//  </copyright>
//  --------------------------------------------------------------------------------------------------------------------

namespace CDPBatchEditor.Commands.Interface
{
    using CDP4Common.EngineeringModelData;

    /// <summary>
    /// Write cvs reports on specified <see cref="EngineeringModel" />
    /// </summary>
    public interface IReportGenerator
    {
        /// <summary>
        /// Report all parameters on the given <see cref="Iteration" />.
        /// </summary>
        void ParametersToCsv();
    }
}
