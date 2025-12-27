> **Status:** Active development and maintenance.

# 0piSound

**0piSound** is a real-time audio visualizer designed for creative and experimental projects.  
It captures system audio via loopback and displays multiple types of spectrum visualizations.

---

## Features

- Real-time audio capture from your system
- Multiple spectrum displays (including spectrogram)
- Lightweight and focused on visualization

---

## Security / Antivirus Note

Some antivirus software, including Windows Defender, may flag the compiled binaries as potential threats.  
**False positive alert** explanation:

- The program captures system audio in real-time (loopback)
- Creates temporary memory buffers for visualization
- No persistence, no network communication, no malicious commands

> This is normal behavior for an audio visualizer using low-level audio hooks.  
> The source code is provided for transparency.

---

## Tech Stack

- **Language:** C#  
- **Framework:** Windows Forms Application (Visual Studio)  
- **Libraries / Packages:** NAudio, Newtonsoft.Json, Costura.Fody  
- **Platform:** Windows only

---

## License

This project is licensed under the **MIT License** – see the [LICENSE](LICENSE) file for details.

---
