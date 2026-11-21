# LaserGRBL [![Donation](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/donate?business=4WQX8HUBXRVUU&no_recurring=0&item_name=LaserGRBL&currency_code=EUR)
Official website [http://lasergrbl.com](http://lasergrbl.com)

LaserGRBL is a Windows GUI for [GRBL](https://github.com/gnea/grbl/wiki). Unlike other GUI LaserGRBL it is specifically developed for use with laser cutter and engraver. In order to use all of LaserGRBL feature, your engraver must supports laser power modulation through gcode "S" command. LaserGRBL is compatible with [Grbl v0.9](https://github.com/grbl/grbl/) and [Grbl v1.1](https://github.com/gnea/grbl/)

All downloads available at https://github.com/arkypita/LaserGRBL/releases

### Support and Donation

Do you like LaserGRBL? Support development with your donation!

[![Donate](https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif)](https://www.paypal.com/donate?business=4WQX8HUBXRVUU&no_recurring=0&item_name=LaserGRBL&currency_code=EUR)

### Existing Features

- GCode file loading with engraving/cutting job preview (with alpha blending for grayscale engraving)
- Image import (jpg, bmp...) with line by line GCode generation (horizontal, vertical, and diagonal).
- Image import (jpg, bmp...) with Vectorization!
- Image import (jpg, bmp...) with 1bit dithering, best result with low power laser
- Vector file import (svg only) [Experimental]
- Different color scheme optimized for different safety glasses
- User defined buttons, power to you!
- Grbl Configuration Import/Export
- Configuration, Alarm and Error codes decoding for Grbl v1.1 (with description tooltip)
- Homing button, Feed Hold button, Resume button and Grbl Reset button
- Job time preview and realtime projection
- Jogging (for any Grbl version)
- Feed overrides (for Grbl > v1.1) with easy-to-use interface
- Support for [WiFi connection via ESP8266 WebSocket](http://lasergrbl.com/en/usage/wifi-with-esp8266/)

### Usage

* [LaserGRBL User Interface](http://lasergrbl.com/usage/user-interface/)
* [Connect to arduino-grbl](http://lasergrbl.com/usage/arduino-connection/)
* [Load G-Code and send to machine](http://lasergrbl.com/usage/load-and-send/)
* [Speed and power overrides](http://lasergrbl.com/usage/overrides/)
* [Jogging](http://lasergrbl.com/usage/jogging/)
* [Custom buttons](http://lasergrbl.com/usage/custom-buttons/)
* [Raster Image Import](http://lasergrbl.com/usage/raster-image-import/)
* [Grayscale conversion parameters](http://lasergrbl.com/usage/raster-image-import/import-parameters/)
* [Line 2 line grayscale conversion](http://lasergrbl.com/usage/raster-image-import/line-to-line-tool/)
* [1bit dithering conversion](http://lasergrbl.com/usage/raster-image-import/dithering-tool/)
* [Image vectorization](http://lasergrbl.com/usage/raster-image-import/vectorization-tool/)

### Job passes with separate power and speed

To vary the scan pattern, choose **Line To Line** or **Dithering** when importing
an image. In the size and laser options dialog, open **Job passes...** and set
each row's direction, lines/mm, power, and speed. Each pass is generated at the
same physical size and origin. Run the result once or use **Quick Save** to
export the entire sequence as G-code. Project files retain the import pass settings.
For a job loaded from one raster image, you can also edit the scan settings with
**Job passes** in the main window. The editor opens in the preview pane and
**Apply** returns to the bed preview. The app rebuilds the loaded job from the
imported image. G-code and jobs assembled by appending files use the power and
speed controls for the existing toolpath.

Use **Edit image** in the main window to reopen the current raster job's
crop and image adjustments. Choose **Next** to review size, origin, power,
speed, and passes, then **Apply** to replace the toolpath. **Undo** and **Redo**
restore up to five recent applied edits while the job is open, including
changes made directly through **Job passes**. Save a project to keep the
current image and settings across sessions.

In **Job passes**, use **Save as...** to name the settings shown in the pass
table. Choose a saved profile and click **Load** to fill the table, then **Apply**
to use it. Raster profiles include scan direction and lines per millimeter.
Profiles for existing toolpaths are listed separately and store power and speed.

### Job operations and playback

Import the tile design with its raster passes, then use **File > Append File**
to add an SVG outline. Open **Operations** in the main window to name, enable, reorder, or set
power, speed and repeats on each operation. Keep both operations at one repeat
when the raster operation already contains the three engraving passes. For
SVGs, **Per contour** repeats each path before proceeding to the next.
Use **Place selected** for drag positioning, numeric X/Y, scale and rotation.
For a single raster image, **Place** adjusts placement and size and supports
undo and redo. Save an operations project to reopen the operation list, or use
**Quick Save** for the compiled G-code.

**Playback** simulates the loaded toolpath and shows estimated laser-on,
travel and dwell time. Select an operation or raster pass, scrub to an estimated
timestamp and adjust playback speed. **Camera** shows a live preview for local
webcams exposed by a compatible Windows video capture driver. Both views use the
main preview pane. Choose **Back** to return to the bed or the operations list.

To repeat an existing toolpath, choose **File > Save (Advanced Options) > Job passes**.
Enable **Set power and speed for each pass**, use **Add pass** (or the pass count),
and set each row's power and speed. The **50% / 100% / 50%** preset creates three
passes with those powers and the original speed. Choose **Apply**, then **Save**
to write the complete sequence into one G-code file.

Both values are percentages of the imported settings: 100% preserves the source,
50% power halves its S values, and 200% speed doubles its F values. Import at full
power if the 100% pass should use full laser power. Grayscale differences and
laser-off commands are preserved. The path is repeated exactly as with normal passes.

For direct execution, use **Job passes...** below the job's progress bar. Applying
changes resets the job's resume position; loading another file clears the plan.
Quick Save and project saving do not embed this plan. A file saved through Advanced
Options already contains all passes, so run that saved file with the repeat count at 1.

### Calibration tests and the Material DB

The **Generate** menu has two new tests next to the power/speed and cutting tests.
**Focus test** burns one short line per Z offset, centred on the current height,
using relative Z moves, so no Z home is needed. Clear **Use the Z axis** for the
ramp method: raise one end of the material, burn one long line with millimetre
ticks, and read the distance at its thinnest point. **Line interval test** fills
one patch per lines/mm value at a fixed power and speed.

After burning a power/speed, cutting or line interval test, choose **Generate >
Save test result to Material DB**. Pick the cell you liked, using its row and
column labels, and the laser model, material, action and thickness. The row is
added to the existing Material DB (or replaces the matching row), so the import
dialogs' material button can use it. The Material DB stores power as a percentage
of `$30`, so the exact S tested is also written to the remarks. Lines/mm, kerf and
laser mode are kept beside the database, not inside it, so `UserMaterials.psh`
stays readable by other LaserGRBL versions.

In **Operations**, **Apply material...** fills repeats, power, speed and kerf for the
selected operation from a Material DB row. Power and speed become percentages of
the operation's own S and F, so a toolpath imported at S300 can't be raised to S900.
The status line says when that happens.

### Kerf compensation

For SVG outlines, set **Kerf mm** in **Operations** to the cut width. Closed outlines
move outward and holes move inward by half the kerf, so parts and holes keep their
drawn size. Arcs are flattened to 0.005 mm. Open paths, holes smaller than the kerf,
and paths that change speed or power partway are left as drawn, and the status
line reports how many. Kerf is applied after placement and is saved in operations
projects.

### Home before the first job

With homing enabled (`$22=1`), **Grbl > Home before first job after connecting**
(on by default) sends `$H` before the first job if the machine hasn't homed since
connecting, or since an alarm. When a work offset is set, LaserGRBL asks first: an
origin set with **Set new zero** before homing would move after `$H`, so in that
case choose **No** to start without homing. A manual home, or one already done
since connecting, skips the step.

### Development Roadmap

Development status and roadmap can be found here: [Roadmap](https://github.com/arkypita/LaserGRBL/issues/64)

### Missing Features

- Minimal Z axis control (LaserGRBL is for XY machine)

#### Screenshot and videos

New OpenGL preview:
![MainForm](https://github.com/GabeMx5/LaserGRBL/assets/86555013/49d042c3-369d-40cc-b4de-57e03a38842a)

[<img src="https://cloud.githubusercontent.com/assets/8782035/23578353/fba95768-00d4-11e7-9357-99c00a30631d.jpg">](https://www.youtube.com/watch?v=Uk2fGoNL3Yk)

![Galeon](https://cloud.githubusercontent.com/assets/8782035/21349915/dba84a5a-c6b4-11e6-965f-a74fd283267a.jpg)

![Raster2Laser](https://cloud.githubusercontent.com/assets/8782035/21425748/34400d46-c84b-11e6-99e5-6eb529a98f8f.jpg)

![Alpha](https://cloud.githubusercontent.com/assets/8782035/21351296/1df460c2-c6bc-11e6-8eee-4612bb7978fa.jpg)

![FinalWork](https://cloud.githubusercontent.com/assets/8782035/21907662/bbe988be-d910-11e6-9bdb-75b6e3404e0a.jpg)

![UserDefinedButtons](https://cloud.githubusercontent.com/assets/8782035/23375844/238e5f70-fd2a-11e6-8826-5ff7743bbea0.jpg)

### Compiling

LaserGRBL is written in C# for .NET Framework 3.5 (or higher) and can be compiled with [SharpDevelop](http://www.icsharpcode.net/opensource/sd/) and of course with [Microsoft Visual Studio](https://www.visualstudio.com) IDE/Compiler

### Licensing

LaserGRBL is free software, released under the [GPLv3 license](https://www.gnu.org/licenses/gpl-3.0.en.html).

### Credits and Contribution

LaserGRBL contains some code from:
- [ColorSlider](https://www.codeproject.com/articles/17395/owner-drawn-trackbar-slider) - Copyright Michal Brylka
- [CsPotrace](https://drawing3d.de/Downloads.aspx) - Copyright Peter Selinger, port by Wolfgang Nagl
- [Bezier2Biarc](https://github.com/domoszlai/bezier2biarc) - Copyright Laszlo
- [websocket-sharp](https://github.com/sta/websocket-sharp) - Copyright sta.blockhead
- [Expression Evaluator](https://github.com/vubiostat/expression.cs) - Copyright Will Gray, Jeremy Roberts
- [GCodeFromSVG](https://github.com/svenhb/GRBL-Plotter) - Copyright Sven Hasemann
- [MS SVG Library](https://archive.codeplex.com/?p=svg) - Microsoft Public License
- [Clipper](http://www.angusj.com/delphi/clipper.php) - Angus Johnson. Copyright © 2010-2014
- [SharpGL](https://github.com/dwmkerr/sharpgl) - Dave Kerr
- [MDI Icons](https://pictogrammers.com/library/mdi/) - Pictogrammers

Thanks to:
- Myself, for italian and english language
- Fernando Luna, sqall123, for spanish translation
- Olivier Salvador, guillaume-rico [#848](https://github.com/arkypita/LaserGRBL/pull/848) for french translation
- Gerd Vogel, for german translation
- Anders Lassen, for danish translation
- Gerson Koppe, for brasilian translation
- Alexey Golovin, Newcomere, AlexeyBond, for russian translation
- Yang Haiqiang, for chinese translation
- 00alkskodi00, for slovak translation [#670](https://github.com/arkypita/LaserGRBL/issues/670)
- ddogman, for hungarian translation [#735](https://github.com/arkypita/LaserGRBL/issues/735)
- Petr Bitnar, for czech translation
- Ozzybanan, for polish translation
- onmaker, for traditional chinese translation [#1066](https://github.com/arkypita/LaserGRBL/pull/1066)
- Nikolaos Ntekas, for Greek translation [#1234](https://github.com/arkypita/LaserGRBL/pull/1234)
- Mrjavaci, for Turkish translation [#1293](https://github.com/arkypita/LaserGRBL/pull/1293)
- Filippo Rivato for code contribution [#305](https://github.com/arkypita/LaserGRBL/pull/305) and again [#1251](https://github.com/arkypita/LaserGRBL/pull/1251)
- Fabio Ferretti for code contribution [#592](https://github.com/arkypita/LaserGRBL/pull/592)
- guillaume-rico for code contribution on Smoothie support
- Tobias Falkner, for code contribution [#937](https://github.com/arkypita/LaserGRBL/pull/937)
- gmmanonymus111, for code contribution [#1032](https://github.com/arkypita/LaserGRBL/pull/1032)
- [GabeMx5](https://github.com/GabeMx5), for OpenGL preview and svg icons implementation
- [jcurl](https://github.com/jcurl), for [RJCP.SerialPort](https://github.com/jcurl/RJCP.DLL.SerialPortStream) implementation
