import os
import sys
import subprocess
import shutil
import tempfile
import datetime
import time
import stat
import ctypes

class SystemInterface:
    def __init__(self):
        self.temp_dir = tempfile.gettempdir()
        self.log_name = f"scan_debug_{int(time.time())}.log"
        self.crash_name = f"crash_tape_{int(time.time())}.err"
        self.temp_log_path = os.path.join(self.temp_dir, self.log_name)
        self.temp_crash_path = os.path.join(self.temp_dir, self.crash_name)
        self.final_log_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
        self.is_gui_mode = "--gui-mode" in sys.argv
        self.exclusions_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mapper_exclusions.txt")

    def _timestamp(self):
        return datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")

    def show_alert(self, title, message, is_error=False):
        icon = 0x10 if is_error else 0x40
        try:
            ctypes.windll.user32.MessageBoxW(0, message, title, icon | 0x0)
        except:
            pass

    def ask_exclusions(self):
        root = os.path.dirname(os.path.abspath(__file__))
        browser = FileBrowserExcluder(root, self.exclusions_file)
        return browser.run()

    def ask_view_mode(self):
        try:
            STD_OUTPUT_HANDLE = -11
            console_handle = ctypes.windll.kernel32.GetStdHandle(STD_OUTPUT_HANDLE)
            ctypes.windll.kernel32.SetConsoleTitleW("Project Mapper - Select View")
            
            print("\nSelect View Mode:")
            print("1. Tree View (Hierarchy with Icons)")
            print("2. Table View (Columns)")
            print("3. Detail View (Full Info)")
            print("4. Simple View (Path Only)")
            choice = input("\nEnter 1-4: ").strip()
            
            if choice == "1": return "tree"
            if choice == "2": return "table"
            if choice == "3": return "detail"
            if choice == "4": return "simple"
            return "tree"
        except:
            return "tree"

    def log(self, message):
        entry = f"[{self._timestamp()}] [INFO] {message}\n"
        try:
            with open(self.temp_log_path, "a", encoding="utf-8") as f:
                f.write(entry)
        except Exception:
            pass
        if not self.is_gui_mode: 
            print(message)

    def log_error(self, message):
        entry = f"[{self._timestamp()}] [ERROR] {message}\n"
        try:
            with open(self.temp_crash_path, "a", encoding="utf-8") as f:
                f.write(entry)
            with open(self.temp_log_path, "a", encoding="utf-8") as f:
                f.write(entry)
        except Exception:
            pass

    def finalize_logs(self):
        target_dir = os.path.dirname(os.path.abspath(__file__))
        if not os.access(target_dir, os.W_OK):
             return self.temp_log_path

        if not os.path.exists(self.final_log_dir):
            try:
                os.makedirs(self.final_log_dir)
            except OSError:
                return self.temp_log_path

        final_log_path = os.path.join(self.final_log_dir, self.log_name)
        final_crash_path = os.path.join(self.final_log_dir, self.crash_name)

        try:
            if os.path.exists(self.temp_log_path):
                shutil.move(self.temp_log_path, final_log_path)
            if os.path.exists(self.temp_crash_path):
                shutil.move(self.temp_crash_path, final_crash_path)
                with open(final_log_path, "a", encoding="utf-8") as f_log:
                    with open(final_crash_path, "r", encoding="utf-8") as f_crash:
                        f_log.write("\n=== CRASH TAPE MERGE ===\n")
                        f_log.write(f_crash.read())
            return final_log_path
        except Exception:
            return self.temp_log_path

class BlackBoxWrapper:
    def __init__(self, interface):
        self.interface = interface

    def launch(self, mode):
        if self.interface.is_gui_mode:
            return True

        current_script = os.path.abspath(__file__)
        env = os.environ.copy()
        
        cmd = [sys.executable, current_script, "--gui-mode", mode]
        
        creation_flags = 0x08000000 if os.name == 'nt' else 0

        try:
            subprocess.Popen(
                cmd,
                stderr=subprocess.PIPE,
                creationflags=creation_flags,
                env=env,
                close_fds=True
            )
            sys.exit(0)
        except Exception as e:
            return False

class FileNode:
    def __init__(self, name, path, is_dir, depth, stats=None):
        self.name = name
        self.path = path
        self.is_dir = is_dir
        self.depth = depth
        self.extension = os.path.splitext(name)[1].lower() if not is_dir else ""
        self.size = 0
        self.permissions = "N/A"
        self.created = 0.0
        self.modified = 0.0
        if stats: self._apply_stats(stats)
        self.icon = self._get_icon()

    def _apply_stats(self, stat_info):
        try:
            self.size = stat_info.st_size
            self.permissions = stat.filemode(stat_info.st_mode)
            self.created = stat_info.st_ctime
            self.modified = stat_info.st_mtime
        except Exception:
            self.size = -1

    def _get_icon(self):
        if self.is_dir: return "📁"
        ext = self.extension
        if ext in ['.py', '.js', '.c', '.cpp', '.java', '.html', '.css', '.php', '.ts']: return "💻"
        if ext in ['.txt', '.md', '.log', '.rst']: return "📝"
        if ext in ['.png', '.jpg', '.jpeg', '.gif', '.svg', '.bmp', '.ico']: return "🖼️"
        if ext in ['.mp3', '.wav', '.ogg', '.flac']: return "🎵"
        if ext in ['.mp4', '.avi', '.mov', '.mkv']: return "🎬"
        if ext in ['.zip', '.rar', '.7z', '.tar', '.gz']: return "📦"
        if ext in ['.exe', '.msi', '.bat', '.sh', '.cmd']: return "⚙️"
        if ext in ['.pdf', '.doc', '.docx', '.xls', '.xlsx']: return "📄"
        return "📄"

    def get_readable_size(self):
        if self.is_dir: return "<DIR>"
        if self.size < 0: return "ERR"
        size = float(self.size)
        for unit in ['B', 'KB', 'MB', 'GB']:
            if size < 1024.0: return f"{size:.2f} {unit}"
            size /= 1024.0
        return f"{size:.2f} TB"

class ScannerCore:
    def __init__(self, interface, root_path, exclusions=None):
        self.interface = interface
        self.root_path = root_path
        self.exclusions = exclusions or []
        self.scanned_count = 0
        self.directories_count = 0

    def _is_excluded(self, name):
        import fnmatch
        for pattern in self.exclusions:
            if fnmatch.fnmatch(name, pattern) or fnmatch.fnmatch(name.lower(), pattern.lower()):
                return True
        return False

    def scan(self):
        try:
            root_stat = os.stat(self.root_path)
            root_node = FileNode(os.path.basename(self.root_path), self.root_path, True, 0, root_stat)
        except:
            root_node = FileNode(os.path.basename(self.root_path), self.root_path, True, 0)

        stack = [root_node]
        
        while stack:
            current_node = stack.pop()
            yield current_node
            self.scanned_count += 1
            
            if current_node.is_dir:
                try:
                    with os.scandir(current_node.path) as it:
                        children = []
                        for entry in it:
                            try:
                                if self._is_excluded(entry.name):
                                    continue
                                s = entry.stat()
                                node = FileNode(entry.name, entry.path, entry.is_dir(), current_node.depth + 1, s)
                                children.append(node)
                            except OSError: continue
                        children.sort(key=lambda x: (not x.is_dir, x.name.lower()), reverse=True)
                        stack.extend(children)
                        self.directories_count += 1
                except PermissionError: pass
                except OSError: pass

class ViewEngine:
    def __init__(self, mode="tree"):
        self.mode = mode

    def render_header(self):
        legend = (
            "LEGEND & ICONS:\n"
            "📁 Folder  | 💻 Code   | 📝 Text\n"
            "🖼️ Image   | 🎵 Audio  | 🎬 Video\n"
            "📦 Archive | ⚙️ Exec   | 📄 Doc/File\n"
            "---------------------------------------------------\n"
        )
        if self.mode == "table":
            headers = f"{'Name':<50} | {'Ext':<6} | {'Size':<10} | {'Perms':<10} | {'Modified':<19} | {'Icon'}"
            return f"\n{legend}{headers}\n{'='*115}\n"
        return f"\n{legend}Report Mode: {self.mode.upper()}\n{'='*60}\n"

    def render_node(self, node):
        if self.mode == "tree": return self._render_tree(node)
        elif self.mode == "table": return self._render_table(node)
        elif self.mode == "detail": return self._render_detail(node)
        return self._render_simple(node)

    def _render_tree(self, node):
        indent = "    " * node.depth
        size_info = f" ({node.get_readable_size()})" if not node.is_dir else ""
        return f"{indent}|-- {node.icon} {node.name}{size_info}\n"

    def _render_table(self, node):
        name = (node.name[:47] + '..') if len(node.name) > 47 else node.name
        size_str = node.get_readable_size()
        ext_str = node.extension.replace(".", "")[:5]
        mod_date = datetime.datetime.fromtimestamp(node.modified).strftime('%Y-%m-%d %H:%M:%S')
        return f"{name:<50} | {ext_str:<6} | {size_str:<10} | {node.permissions:<10} | {mod_date:<19} | {node.icon}\n"

    def _render_detail(self, node):
        return (
            f"File: {node.icon} {node.name}\n"
            f"Path: {node.path}\n"
            f"Type: {'Directory' if node.is_dir else 'File'}\n"
            f"Extension: {node.extension}\n"
            f"Size: {node.get_readable_size()} ({node.size} bytes)\n"
            f"Permissions: {node.permissions}\n"
            f"Created: {datetime.datetime.fromtimestamp(node.created)}\n"
            f"Modified: {datetime.datetime.fromtimestamp(node.modified)}\n"
            f"{'-'*60}\n"
        )

    def _render_simple(self, node):
        return f"{node.icon} {node.path}  [{node.get_readable_size()}]\n"

class FileBrowserExcluder:
    def __init__(self, root_path, exclusions_file):
        self.root_path = root_path
        self.exclusions_file = exclusions_file
        self.current_dir = root_path
        self.exclusions = set()
        self.cursor = 0
        self.entries = []
        self._load_exclusions()

    def _setup_terminal(self):
        if os.name == "nt":
            h_out = ctypes.windll.kernel32.GetStdHandle(-11)
            ctypes.windll.kernel32.SetConsoleMode(h_out, 0x0001 | 0x0002 | 0x0004)
            h_in = ctypes.windll.kernel32.GetStdHandle(-10)
            self._old_in_mode = ctypes.c_ulong(0)
            ctypes.windll.kernel32.GetConsoleMode(h_in, ctypes.byref(self._old_in_mode))
            ctypes.windll.kernel32.SetConsoleMode(h_in, self._old_in_mode.value & ~0x0006)
        sys.stdout.write("\x1b[?25l")
        sys.stdout.flush()

    def _restore_terminal(self):
        sys.stdout.write("\x1b[?25h")
        sys.stdout.flush()
        if os.name == "nt" and hasattr(self, "_old_in_mode"):
            h_in = ctypes.windll.kernel32.GetStdHandle(-10)
            ctypes.windll.kernel32.SetConsoleMode(h_in, self._old_in_mode.value)

    def _load_exclusions(self):
        if os.path.exists(self.exclusions_file):
            try:
                with open(self.exclusions_file, "r", encoding="utf-8") as f:
                    for line in f:
                        line = line.strip()
                        if line:
                            self.exclusions.add(line)
            except Exception:
                pass

    def _save_exclusions(self):
        try:
            with open(self.exclusions_file, "w", encoding="utf-8") as f:
                for excl in sorted(self.exclusions):
                    f.write(excl + "\n")
        except Exception:
            pass

    def _load_entries(self):
        self.entries = []
        try:
            with os.scandir(self.current_dir) as it:
                items = list(it)
            dirs = sorted([e for e in items if e.is_dir()], key=lambda x: x.name.lower())
            files = sorted([e for e in items if not e.is_dir()], key=lambda x: x.name.lower())
            self.entries = dirs + files
        except Exception:
            pass
        self.cursor = min(self.cursor, max(0, len(self.entries) - 1))

    def _get_icon(self, entry):
        if entry.is_dir():
            return "[DIR]"
        ext = os.path.splitext(entry.name)[1].lower()
        if ext in [".py", ".js", ".c", ".cpp", ".java", ".html", ".css", ".ts"]: return "[CODE]"
        if ext in [".txt", ".md", ".log", ".rst"]: return "[TEXT]"
        if ext in [".png", ".jpg", ".jpeg", ".gif", ".svg", ".bmp"]: return "[IMG] "
        if ext in [".zip", ".rar", ".7z", ".tar", ".gz"]: return "[ARC] "
        if ext in [".exe", ".msi", ".bat", ".sh", ".cmd"]: return "[EXEC]"
        if ext in [".pdf", ".doc", ".docx", ".xls", ".xlsx"]: return "[DOC] "
        return "[FILE]"

    def _is_name_excluded(self, name):
        return name in self.exclusions

    def _is_ext_excluded(self, entry):
        if entry.is_dir():
            return False
        ext = os.path.splitext(entry.name)[1].lower()
        return bool(ext) and f"*{ext}" in self.exclusions

    def _render(self):
        sys.stdout.write("\x1b[H\x1b[2J")
        sys.stdout.flush()
        rel = os.path.relpath(self.current_dir, self.root_path)
        if rel == ".":
            rel = "[root]"

        print("=" * 64)
        print("  Project Mapper  --  Exclusion Browser")
        print(f"  Location: {rel}")
        print("=" * 64)

        if not self.entries:
            print("  (empty directory)")
        else:
            for i, entry in enumerate(self.entries):
                icon = self._get_icon(entry)
                name = entry.name
                name_excl = self._is_name_excluded(name)
                ext_excl = self._is_ext_excluded(entry)
                if name_excl:
                    marker = "[X]"
                elif ext_excl:
                    marker = "[~]"
                else:
                    marker = "[ ]"
                arrow = ">" if i == self.cursor else " "
                ext = os.path.splitext(name)[1].lower()
                ext_note = f"  (* {ext} type excluded)" if ext_excl and not name_excl else ""
                display_name = (name[:38] + "..") if len(name) > 40 else name
                print(f"  {arrow} {marker} {icon} {display_name}{ext_note}")

        print("=" * 64)
        print("  [UP/DOWN] Navigate   [SPACE] Toggle name   [T] Toggle *.ext")
        print("  [ENTER]  Open dir    [BKSP]  Go up         [D] Done & save")
        print("=" * 64)
        excl_sorted = sorted(self.exclusions)
        if excl_sorted:
            chunks = []
            line = ""
            for e in excl_sorted:
                segment = (", " if line else "") + e
                if len(line) + len(segment) > 58:
                    chunks.append(line)
                    line = e
                else:
                    line += segment
            if line:
                chunks.append(line)
            print(f"  Excluded ({len(self.exclusions)}):")
            for chunk in chunks:
                print(f"    {chunk}")
        else:
            print("  Excluded (0): (none)")
        print("=" * 64)

    def run(self):
        try:
            import msvcrt
        except ImportError:
            raw = input("Exclusion patterns (comma-separated): ").strip()
            result = [p.strip() for p in raw.split(",") if p.strip()]
            self.exclusions = set(result)
            self._save_exclusions()
            return result

        self._setup_terminal()
        self._load_entries()

        while True:
            self._render()
            key = msvcrt.getch()

            if key == b"\xe0":
                key2 = msvcrt.getch()
                if key2 == b"H":
                    self.cursor = max(0, self.cursor - 1)
                elif key2 == b"P":
                    self.cursor = min(len(self.entries) - 1, self.cursor + 1)
            elif key == b" ":
                if self.entries:
                    name = self.entries[self.cursor].name
                    if name in self.exclusions:
                        self.exclusions.discard(name)
                    else:
                        self.exclusions.add(name)
            elif key in (b"\r", b"\n"):
                if self.entries and self.entries[self.cursor].is_dir():
                    self.current_dir = self.entries[self.cursor].path
                    self.cursor = 0
                    self._load_entries()
            elif key == b"\x08":
                parent = os.path.dirname(self.current_dir)
                if os.path.abspath(parent) != os.path.abspath(self.current_dir):
                    if os.path.abspath(self.current_dir) != os.path.abspath(self.root_path):
                        self.current_dir = parent
                        self.cursor = 0
                        self._load_entries()
            elif key in (b"t", b"T"):
                if self.entries and not self.entries[self.cursor].is_dir():
                    ext = os.path.splitext(self.entries[self.cursor].name)[1].lower()
                    if ext:
                        pattern = f"*{ext}"
                        if pattern in self.exclusions:
                            self.exclusions.discard(pattern)
                        else:
                            self.exclusions.add(pattern)
            elif key in (b"d", b"D", b"\x1b"):
                self._restore_terminal()
                self._save_exclusions()
                return list(self.exclusions)


if __name__ == "__main__":
    sys_interface = SystemInterface()
    
    if "--gui-mode" not in sys.argv:
        mode = sys_interface.ask_view_mode()
        exclusions = sys_interface.ask_exclusions()
        exclusions_arg = ",".join(exclusions) if exclusions else ""
        
        current_script = os.path.abspath(__file__)
        env = os.environ.copy()
        cmd = [sys.executable, current_script, "--gui-mode", mode, "--exclude", exclusions_arg]
        creation_flags = 0x08000000 if os.name == 'nt' else 0
        try:
            subprocess.Popen(cmd, stderr=subprocess.PIPE, creationflags=creation_flags, env=env, close_fds=True)
        except Exception:
            pass
        sys.exit(0)
    else:
        mode = sys.argv[2] if len(sys.argv) > 2 else "tree"
        exclude_index = sys.argv.index("--exclude") if "--exclude" in sys.argv else -1
        exclusions_raw = sys.argv[exclude_index + 1] if exclude_index != -1 and exclude_index + 1 < len(sys.argv) else ""
        exclusions = [p for p in exclusions_raw.split(",") if p] if exclusions_raw else []
        
        sys_interface.show_alert("Project Mapper", f"Scanning: {os.path.dirname(os.path.abspath(__file__))}")

        current_script_dir = os.path.dirname(os.path.abspath(__file__))
        scanner = ScannerCore(sys_interface, current_script_dir, exclusions)
        view_engine = ViewEngine(mode)
        
        report_filename = f"project_map_{int(time.time())}.txt"
        
        report_path = os.path.join(current_script_dir, report_filename)
        file_handle = None
        
        try:
            file_handle = open(report_path, "w", encoding="utf-8")
        except PermissionError:
            desktop = os.path.join(os.path.join(os.environ['USERPROFILE']), 'Desktop') 
            report_path = os.path.join(desktop, report_filename)
            try:
                file_handle = open(report_path, "w", encoding="utf-8")
            except:
                 report_path = os.path.join(tempfile.gettempdir(), report_filename)
                 file_handle = open(report_path, "w", encoding="utf-8")

        try:
            sys_interface.log(f"Scanner Initiated. Mode: {mode}")

            with file_handle:
                file_handle.write(f"Project Map Generated: {sys_interface._timestamp()}\n")
                file_handle.write(f"Root: {current_script_dir}\n")
                file_handle.write(view_engine.render_header())
                
                for node in scanner.scan():
                    if node.path == current_script_dir: continue
                    file_handle.write(view_engine.render_node(node))
            
            sys_interface.finalize_logs()
            sys_interface.show_alert("Scan Complete", f"Done!\n\nFiles: {scanner.scanned_count}\nSaved to: {report_path}")
            
        except Exception as e:
            sys_interface.log_error(f"Critical Failure: {str(e)}")
            log_path = sys_interface.finalize_logs()
            sys_interface.show_alert("Error", f"Script crashed.\nLog saved to: {log_path}", is_error=True)
            sys.exit(1)