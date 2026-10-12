;;; ClearAttributes.lsp
;;; Empties the value of every attribute in the blocks you select.
;;; The blocks and their attribute tags stay - only the text is cleared.
;;;
;;; Load: APPLOAD (or drag this file into AutoCAD), then type CLEARATTS.

(vl-load-com)

(defun c:CLEARATTS (/ doc ss i blk nblk natt)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (princ "\nSelect the blocks to clear (only blocks with attributes are picked up): ")
  (if (setq ss (ssget '((0 . "INSERT") (66 . 1))))
    (progn
      (vla-StartUndoMark doc)
      (setq nblk 0 natt 0)
      (repeat (setq i (sslength ss))
        (setq blk (vlax-ename->vla-object (ssname ss (setq i (1- i)))))
        (foreach att (vlax-invoke blk 'GetAttributes)
          (if (/= (vla-get-TextString att) "")
            (progn (vla-put-TextString att "") (setq natt (1+ natt)))
          )
        )
        (setq nblk (1+ nblk))
      )
      (vla-EndUndoMark doc)
      (princ (strcat "\nCleared " (itoa natt) " attribute value(s) in " (itoa nblk) " block(s)."))
    )
    (princ "\nNo blocks with attributes selected.")
  )
  (princ)
)

(princ "\nClearAttributes loaded. Type CLEARATTS.")
(princ)
