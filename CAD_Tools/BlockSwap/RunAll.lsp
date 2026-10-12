;;; RunAll.lsp - pick a folder, tick the drawings, then it opens each one in turn: AUDIT, BLOCKSWAP,
;;; AUDIT again, then saves and closes it. Run it from a blank drawing (Drawing1).
(setq RA-LSP "C:/CAD/BlockSwap.lsp")
(vl-load-com)
(defun RA:PickFolder (/ sh f r)
  (setq sh (vlax-create-object "Shell.Application")
        f (vlax-invoke-method sh 'BrowseForFolder 0 "Select the sheets folder" 80 0))
  (if f
    (progn
      (setq r (vlax-get-property (vlax-get-property f 'Self) 'Path))
      (vlax-release-object f)))
  (vlax-release-object sh)
  r)
(defun RA:Indexes (n / i l)
  (setq i n)
  (while (> i 0) (setq l (cons (itoa (setq i (1- i))) l)))
  (if l (apply 'strcat (cons (car l) (mapcar '(lambda (x) (strcat " " x)) (cdr l)))) ""))
;; Shows the drawings in a list - pick the ones to run (Ctrl/Shift-click).
(defun RA:PickDrawings (files / dcl f id sel)
  (setq dcl (vl-filename-mktemp "runall.dcl") f (open dcl "w"))
  (foreach l '("runall : dialog { label = \"RunAll - select drawings (Ctrl/Shift-click)\";"
               "  : list_box { key = \"lst\"; multiple_select = true; width = 80; height = 25; }"
               "  : row { : button { key = \"all\"; label = \"Select all\"; }"
               "          : button { key = \"none\"; label = \"Select none\"; } }"
               "  ok_cancel; }")
    (write-line l f))
  (close f)
  (setq id (load_dialog dcl))
  (if (new_dialog "runall" id)
    (progn
      (start_list "lst") (mapcar 'add_list files) (end_list)
      (action_tile "all" "(set_tile \"lst\" (RA:Indexes (length files)))")
      (action_tile "none" "(set_tile \"lst\" \"\")")
      (action_tile "accept" "(setq sel (get_tile \"lst\")) (done_dialog 1)")
      (if (= (start_dialog) 1)
        (setq sel (mapcar '(lambda (i) (nth i files)) (read (strcat "(" sel ")")))))
      (unload_dialog id)))
  (vl-file-delete dcl)
  sel)
(defun c:RUNALL (/ dir files f scr)
  (cond
    ((not (findfile RA-LSP)) (princ (strcat "\nCan't find " RA-LSP)))
    ((not (setq dir (RA:PickFolder))))
    ((not (setq files (vl-sort (vl-directory-files dir "*.dwg" 1) '<)))
      (princ (strcat "\nNo drawings in " dir)))
    ((not (setq files (RA:PickDrawings files)))
      (princ "\nNo drawings selected."))
    (t
      (setq scr (strcat (vl-filename-directory RA-LSP) "/RunAll.scr")
            f (open scr "w"))
      (foreach x files
        (write-line "_.OPEN" f)
        (write-line (strcat "\"" dir "\\" x "\"") f)
        (write-line (strcat "(load \"" RA-LSP "\")") f)
        (write-line "_.AUDIT" f)
        (write-line "_Y" f)
        (write-line "BLOCKSWAP" f)
        (write-line "_.AUDIT" f)
        (write-line "_Y" f)
        (write-line "_.QSAVE" f)
        (write-line "_.CLOSE" f))
      (close f)
      (initget "Yes No")
      (if (/= (getkword (strcat "\nRun BLOCKSWAP on " (itoa (length files)) " drawing(s) in " dir "? [Yes/No] <No>: ")) "Yes")
        (princ "\nCancelled.")
        (command "_.SCRIPT" scr))))
  (princ))
(princ "\nRunAll loaded. Type RUNALL.")
(princ)
