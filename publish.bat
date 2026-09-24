@echo off
echo GutsV Final Surum Paketleniyor...
echo.

:: Release Modunda, Klasör olarak derle (Tek dosya modunda DLL sorunları yasanmamasi icin)
dotnet publish GutsV.UI\GutsV.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o ./Build_Output

:: Rename to user friendly name
cd Build_Output
ren GutsV.exe "GutsV Colour.exe"
cd ..

echo.
echo ========================================================
echo ISLEM TAMAMLANDI!
echo Dosyalariniz "Build_Output" klasorunde hazir.
echo.
echo GutsV Colour.exe uygulamasini istediginiz yere tasiyabilirsiniz.
echo Ilk calistirmada otomatik olarak baslangica (Registry) eklenecektir.
echo ========================================================
pause
