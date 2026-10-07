Name:           bulk-rename-utility
Version:        %{?pkgversion}%{!?pkgversion:1.0.0}
Release:        1%{?dist}
Summary:        Preview-first batch file renaming utility
License:        Unlicense
%global debug_package %{nil}
Source0:        Bulk_Rename_Utility_Linux-%{version}.tar.gz
BuildRequires:  dotnet-sdk-10.0
Requires:       fontconfig
Requires:       libICE
Requires:       libSM
Requires:       libX11
Requires:       libXext
Requires:       libXrender
Requires:       libXrandr
Requires:       libXcursor
Requires:       libXi

%description
Desktop tool for applying and previewing batch filename transformations.

%prep
%setup -q -c

%build
dotnet publish src/BulkRenameUtility/BulkRenameUtility.csproj \
    --configuration Release --runtime linux-x64 --self-contained true \
    --output publish

%install
install -d %{buildroot}/opt/bulk-rename-utility \
    %{buildroot}/usr/bin \
    %{buildroot}/usr/share/applications \
    %{buildroot}/usr/share/icons/hicolor/512x512/apps
cp -a publish/. %{buildroot}/opt/bulk-rename-utility/
printf '%s\n' '#!/bin/sh' 'exec /opt/bulk-rename-utility/BulkRenameUtility "$@"' \
    > %{buildroot}/usr/bin/BulkRenameUtility
chmod 0755 %{buildroot}/usr/bin/BulkRenameUtility \
    %{buildroot}/opt/bulk-rename-utility/BulkRenameUtility
install -m 0644 packaging/bulk-rename-utility.desktop \
    %{buildroot}/usr/share/applications/bulk-rename-utility.desktop
install -m 0644 src/BulkRenameUtility/Assets/bru-linux.png \
    %{buildroot}/usr/share/icons/hicolor/512x512/apps/bulk-rename-utility.png

%files
/opt/bulk-rename-utility
/usr/bin/BulkRenameUtility
/usr/share/applications/bulk-rename-utility.desktop
/usr/share/icons/hicolor/512x512/apps/bulk-rename-utility.png
