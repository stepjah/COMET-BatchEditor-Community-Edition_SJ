<img src="https://github.com/STARIONGROUP/COMET-BatchEditor-Community-Edition/raw/development/COMET-Community-Edition.jpg" width="250">

The CDP4-COMET BatchEditor Community Editition (CE) is the Starion Group open source command line tool compliant with ECSS-E-TM-10-25 Annex A and Annex C webservices. The BatchEditor allows fast execution of common commands on large prtions of EngineeringModel data.

## Installation

The batch editor can be installed by downloading the zipped package or as a dotnet tool from https://nuget.org.
  - download and Unpack `CDP4_COMET_BatchEditor_Community_Edition_x.y.z` in to a folder on the hard drive and execute commands using any command line console.
  - dotnet tool install --global cdp4-comet-be

## Usage

Find example usages of all commands in [CommandExamples.md](CommandExamples.md) and [wiki](https://github.com/STARIONGROUP/COMET-BatchEditor-Community-Edition/wiki).

## Build status

GitHub actions are used to build and test the libraries

Branch | Build Status
------- | :------------
Master | ![Build Status](https://github.com/STARIONGROUP/COMET-BatchEditor-Community-Edition/actions/workflows/CodeQuality.yml/badge.svg?branch=master)
Development | ![Build Status](https://github.com/STARIONGROUP/COMET-BatchEditor-Community-Edition/actions/workflows/CodeQuality.yml/badge.svg?branch=development)

## SonarQube Status:
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=sqale_rating)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=reliability_rating)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=security_rating)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=coverage)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=duplicated_lines_density)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=bugs)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=ncloc)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=sqale_index)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=STARIONGROUP_COMET-BatchEditor-Community-Edition&metric=vulnerabilities)](https://sonarcloud.io/dashboard?id=STARIONGROUP_COMET-BatchEditor-Community-Edition)

## Concurrent Design

The Concurrent Design method is an approach to design activities in which all design disciplines and stakeholders are brought together to create an integrated design in a collaborative way of working.

The Concurrent Design method brings many advantages to the early design phase by providing a structure for this otherwise chaotic phase. Many design concepts have been implemented in the Concurrent Design method to help a team of stakeholders perform their task. The design work is done in collocated sessions with all stakeholders involved and present, creating an integrated design and enabling good communication and exchange of information between team members.

To read more about Concurrent Design and how to use the CDP4 Desktop application to perform concurrent design please read our documentation at https://comet-dev-docs.mbsehub.org/

## CDP4-COMET-SDK

The CDP4-COMET-BatchEditor Community Edition makes use of the [CDP4-COMET-SDK](https://github.com/STARIONGROUP/COMET-SDK-Community-Edition).

# License

The CDP4-COMET-BatchEditor Community Edition is provided to the community under the GNU Lesser General Public License v3.0. See the license files for the details. The license can be found [here](LICENSE).

The [Starion Group](https://www.stariongroup.eu) also provides the [COMET Web Services Enterprise Edition](https://github.com/STARIONGROUP/COMET-WebServices-Community-Edition/wiki/CDP4-Web-Services-Enterprise-Edition) which comes with commercial support and more features. [Contact](https://www.stariongroup.eu/contact) us for more details.

# Contributions

Contributions to the code-base are welcome. However, before we can accept your contributions we ask any contributor to sign the Contributor License Agreement (CLA) and send this digitaly signed to s.gerene@stariongroup.eu. You can find the CLA's in the CLA folder.
