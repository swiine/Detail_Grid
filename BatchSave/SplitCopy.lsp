;;; SplitCopy.lsp
;;; Step 1 of splitting a master DWG for the Autodesk Batch Save Utility.
;;;
;;; Open the master drawing in AutoCAD / Civil 3D, APPLOAD this file, and run
;;; SPLITCOPY. Going down the layout list, it copies the master DWG once per
;;; drawing (layout) into a "Split" folder next to the master, naming each copy
;;; after its drawing. The master itself is not changed.
;;;
;;; Then run Batch Save Utility on the Split folder with KeepOneLayout.scr.

(defun splitcopy:safe (name)
  ;; Swap characters Windows does not allow in file names for "_".
  (vl-string-translate "<>:\"/\\|?*" "_________" name)
)

(defun c:SPLITCOPY (/ src dir out n)
  (if (/= (getvar "DBMOD") 0)
    (alert "This drawing has unsaved changes.\nThe copies are made from the saved file on disk,\nso save first if you want those changes included.")
  )
  (setq src (strcat (getvar "DWGPREFIX") (getvar "DWGNAME"))
        dir (strcat (getvar "DWGPREFIX") "Split\\")
        n   0)
  (vl-mkdir dir)
  (foreach name (layoutlist)
    (setq out (strcat dir (splitcopy:safe name) ".dwg"))
    (if (findfile out) (vl-file-delete out))
    (if (vl-file-copy src out)
      (progn (setq n (1+ n)) (princ (strcat "\nCopied: " out)))
      (princ (strcat "\nFAILED to copy: " out " (is it open?)"))
    )
  )
  (princ (strcat "\n" (itoa n) " copies made in " dir))
  (princ "\nNext: run Batch Save Utility on that folder with KeepOneLayout.scr")
  (princ)
)

(princ "\nSplitCopy loaded. Type SPLITCOPY to run.")
(princ)
